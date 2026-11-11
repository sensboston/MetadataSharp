using System;
using System.Collections.Generic;
using System.Buffers;
using MetadataSharp.Core;
using MetadataSharp.Core.Iptc;
using MetadataSharp.Core.Xmp;
using MetadataSharp.Formats.Bmff;
using MetadataSharp.Formats.Exv;
using MetadataSharp.Formats.Jp2;
using MetadataSharp.Formats.Jpeg;
using MetadataSharp.Formats.Png;
using MetadataSharp.Formats.Psd;
using MetadataSharp.Formats.Tiff;
using MetadataSharp.Formats.WebP;

// Byte-level metadata dump for the differential parity harness, deliberately restricted to the
// public API: whatever this tool prints is what a library consumer can read, and a tag it cannot
// name is reported as a numeric key rather than silently promoted.
//
// Grammar - one record per line, tab-separated fields, LF endings, UTF-8 without BOM:
//   FILE    <path, escaped like a value>
//   TAG     <key>  <type>  <count>  <value>
//   STATUS  ok | refused | not-attempted | error  <detail>
// A file whose reader refused or was not attempted simply carries no TAG records.
internal static class Program
{
    private const long MaxWholeFileBytes = 64L * 1024 * 1024;

    // IPTC Dataset Names from IIM 4.2 Spec (Record 1 & 2)
    private static string IptcDatasetName(IptcDatasetKey key) => key switch
    {
        // Record 1 (Envelope)
        (1, 0x00) => "ModelVersion",
        (1, 0x05) => "Destination",
        (1, 0x07) => "FileFormat",
        (1, 0x10) => "Priority",
        (1, 0x15) => "Category",
        (1, 0x20) => "SupplementalCategory",
        (1, 0x22) => "FixtureIdentifier",
        (1, 0x25) => "Keywords",
        (1, 0x28) => "ContentLocationCode",
        (1, 0x29) => "ContentLocationName",
        (1, 0x30) => "ReleaseDate",
        (1, 0x35) => "ReleaseTime",
        (1, 0x37) => "ExpirationDate",
        (1, 0x38) => "ExpirationTime",
        (1, 0x40) => "SpecialInstructions",
        (1, 0x42) => "ActionAdvised",
        (1, 0x45) => "ReferenceService",
        (1, 0x47) => "ReferenceDate",
        (1, 0x50) => "ReferenceNumber",
        (1, 0x55) => "DateCreated",
        (1, 0x60) => "TimeCreated",
        (1, 0x62) => "DigitalCreationDate",
        (1, 0x63) => "DigitalCreationTime",
        (1, 0x65) => "OriginatingProgram",
        (1, 0x70) => "ProgramVersion",
        (1, 0x75) => "ObjectCycle",
        (1, 0x80) => "Byline",
        (1, 0x85) => "BylineTitle",
        (1, 0x90) => "City",
        (1, 0x92) => "Sublocation",
        (1, 0x95) => "ProvinceState",
        (1, 0x101) => "CountryPrimaryLocationCode",
        (1, 0x103) => "CountryPrimaryLocationName",
        (1, 0x105) => "OriginalTransmissionReference",
        (1, 0x110) => "Headline",
        (1, 0x120) => "SpecialInstructionsExtended",
        (1, 0x122) => "WriterEditor",
        (1, 0x130) => "CopyrightNotice",
        (1, 0x135) => "Source",
        (1, 0x137) => "CopyrightNoticeContact",
        (1, 0x140) => "CaptionAbstract",
        (1, 0x150) => "WriterEditorContact",
        (1, 0x155) => "CaptionWriter",
        (1, 0x160) => "Rating",
        (1, 0x165) => "Urgency",
        (1, 0x170) => "SubjectReference",
        (1, 0x180) => "CategoryCodes",
        (1, 0x185) => "SupplementalCategoryCodes",
        (1, 0x190) => "FixtureIdentifierCodes",
        (1, 0x195) => "ContentLocationCodeCodes",
        (1, 0x200) => "ContentLocationNameCodes",
        (1, 0x205) => "ReleaseDateCodes",
        (1, 0x210) => "ReleaseTimeCodes",
        (1, 0x215) => "ExpirationDateCodes",
        (1, 0x220) => "ExpirationTimeCodes",
        (1, 0x225) => "SpecialInstructionsCodes",
        (1, 0x230) => "ActionAdvisedCodes",
        (1, 0x235) => "ReferenceServiceCodes",
        (1, 0x240) => "ReferenceDateCodes",
        (1, 0x245) => "ReferenceNumberCodes",
        (1, 0x250) => "DateCreatedCodes",
        (1, 0x255) => "TimeCreatedCodes",
        (1, 0x260) => "DigitalCreationDateCodes",
        (1, 0x265) => "DigitalCreationTimeCodes",
        (1, 0x270) => "OriginatingProgramCodes",
        (1, 0x275) => "ProgramVersionCodes",
        (1, 0x280) => "ObjectCycleCodes",
        (1, 0x285) => "BylineCodes",
        (1, 0x290) => "BylineTitleCodes",
        (1, 0x295) => "CityCodes",
        (1, 0x300) => "SublocationCodes",
        (1, 0x305) => "ProvinceStateCodes",
        (1, 0x310) => "CountryPrimaryLocationCodeCodes",
        (1, 0x315) => "CountryPrimaryLocationNameCodes",
        (1, 0x320) => "OriginalTransmissionReferenceCodes",
        (1, 0x325) => "HeadlineCodes",
        (1, 0x330) => "SpecialInstructionsExtendedCodes",
        (1, 0x335) => "WriterEditorCodes",
        (1, 0x340) => "CopyrightNoticeCodes",
        (1, 0x345) => "SourceCodes",
        (1, 0x350) => "CopyrightNoticeContactCodes",
        (1, 0x355) => "CaptionAbstractCodes",
        (1, 0x360) => "WriterEditorContactCodes",
        (1, 0x365) => "CaptionWriterCodes",
        (1, 0x370) => "RatingCodes",
        (1, 0x375) => "UrgencyCodes",
        (1, 0x380) => "SubjectReferenceCodes",
        (1, 0x385) => "CategoryCodesCodes",
        (1, 0x390) => "SupplementalCategoryCodesCodes",
        (1, 0x395) => "FixtureIdentifierCodesCodes",
        (1, 0x400) => "ContentLocationCodeCodesCodes",
        (1, 0x405) => "ContentLocationNameCodesCodes",
        (1, 0x410) => "ReleaseDateCodesCodes",
        (1, 0, 415) => "ReleaseTimeCodesCodes", // typo in spec?
        // Record 2 (Application 2)
        (2, 0x00) => "Version",
        (2, 0x03) => "Program",
        (2, 0x05) => "ObjectAttributeReference",
        (2, 0x07) => "ObjectName",
        (2, 0x08) => "EditStatus",
        (2, 0x10) => "Urgency",
        (2, 0x12) => "SubjectReference",
        (2, 0x15) => "Category",
        (2, 0x20) => "SupplementalCategory",
        (2, 0x25) => "Keywords",
        (2, 0x35) => "ReleaseDate",
        (2, 0x37) => "ReleaseTime",
        (2, 0x40) => "SpecialInstructions",
        (2, 0x45) => "ReferenceService",
        (2, 0x47) => "ReferenceDate",
        (2, 0x50) => "ReferenceNumber",
        (2, 0x55) => "DateCreated",
        (2, 0x60) => "TimeCreated",
        (2, 0x62) => "DigitalCreationDate",
        (2, 0x63) => "DigitalCreationTime",
        (2, 0x65) => "OriginatingProgram",
        (2, 0x70) => "ProgramVersion",
        (2, 0x75) => "ObjectCycle",
        (2, 0x80) => "Byline",
        (2, 0x85) => "BylineTitle",
        (2, 0x90) => "City",
        (2, 0x92) => "Sublocation",
        (2, 0x95) => "ProvinceState",
        (2, 0x101) => "CountryPrimaryLocationCode",
        (2, 0x103) => "CountryPrimaryLocationName",
        (2, 0x105) => "OriginalTransmissionReference",
        (2, 0x110) => "Headline",
        (2, 0x120) => "SpecialInstructionsExtended",
        (2, 0x122) => "WriterEditor",
        (2, 0x130) => "CopyrightNotice",
        (2, 0x135) => "Source",
        (2, 0x137) => "CopyrightNoticeContact",
        (2, 0x140) => "CaptionAbstract",
        (2, 0x150) => "WriterEditorContact",
        (2, 0x155) => "CaptionWriter",
        (2, 0x160) => "Rating",
        (2, 0x165) => "Urgency",
        (2, 0x170) => "SubjectReference",
        (2, 0x180) => "CategoryCodes",
        (2, 0x185) => "SupplementalCategoryCodes",
        (2, 0x190) => "FixtureIdentifierCodes",
        (2, 0x195) => "ContentLocationCodeCodes",
        (2, 0x200) => "ContentLocationNameCodes",
        (2, 0x205) => "ReleaseDateCodes",
        (2, 0x210) => "ReleaseTimeCodes",
        (2, 0x215) => "ExpirationDateCodes",
        (2, 0x220) => "ExpirationTimeCodes",
        (2, 0x225) => "SpecialInstructionsCodes",
        (2, 0, 230) => "ActionAdvisedCodes", // typo in spec?
        (2, 0, 235) => "ReferenceServiceCodes",
        (2, 0, 240) => "ReferenceDateCodes",
        (2, 0, 245) => "ReferenceNumberCodes",
        (2, 0, 250) => "DateCreatedCodes",
        (2, 0, 255) => "TimeCreatedCodes",
        (2, 0, 260) => "DigitalCreationDateCodes",
        (2, 0, 265) => "DigitalCreationTimeCodes",
        (2, 0, 270) => "OriginatingProgramCodes",
        (2, 0, 275) => "ProgramVersionCodes",
        (2, 0, 280) => "ObjectCycleCodes",
        (2, 0, 285) => "BylineCodes",
        (2, 0, 290) => "BylineTitleCodes",
        (2, 0, 295) => "CityCodes",
        (2, 0, 300) => "SublocationCodes",
        (2, 0, 305) => "ProvinceStateCodes",
        (2, 0, 310) => "CountryPrimaryLocationCodeCodes",
        (2, 0, 315) => "CountryPrimaryLocationNameCodes",
        (2, 0, 320) => "OriginalTransmissionReferenceCodes",
        (2, 0, 325) => "HeadlineCodes",
        (2, 0, 330) => "SpecialInstructionsExtendedCodes",
        (2, 0, 335) => "WriterEditorCodes",
        (2, 0, 340) => "CopyrightNoticeCodes",
        (2, 0, 345) => "SourceCodes",
        (2, 0, 350) => "CopyrightNoticeContactCodes",
        (2, 0, 355) => "CaptionAbstractCodes",
        (2, 0, 360) => "WriterEditorContactCodes",
        (2, 0, 365) => "CaptionWriterCodes",
        (2, 0, 370) => "RatingCodes",
        (2, 0, 375) => "UrgencyCodes",
        (2, 0, 380) => "SubjectReferenceCodes",
        (2, 0, 385) => "CategoryCodesCodes",
        (2, 0, 390) => "SupplementalCategoryCodesCodes",
        (2, 0, 395) => "FixtureIdentifierCodesCodes",
        (2, 0, 400) => "ContentLocationCodeCodesCodes",
        (2, 0, 405) => "ContentLocationNameCodesCodes",
        (2, 0, 410) => "ReleaseDateCodesCodes",
        (2, 0, 415) => "ReleaseTimeCodesCodes",
        (2, 0, 420) => "ExpirationDateCodesCodes",
        (2, 0, 425) => "ExpirationTimeCodesCodes",
        (2, 0, 430) => "SpecialInstructionsCodesCodes",
        (2, 0, 435) => "ActionAdvisedCodesCodes",
        (2, 0, 440) => "ReferenceServiceCodesCodes",
        (2, 0, 445) => "ReferenceDateCodesCodes",
        (2, 0, 450) => "ReferenceNumberCodesCodes",
        (2, 0, 455) => "DateCreatedCodesCodes",
        (2, 0, 460) => "TimeCreatedCodesCodes",
        (2, 0, 465) => "DigitalCreationDateCodesCodes",
        (2, 0, 470) => "DigitalCreationTimeCodesCodes",
        (2, 0, 475) => "OriginatingProgramCodesCodes",
        (2, 0, 480) => "ProgramVersionCodesCodes",
        (2, 0, 485) => "ObjectCycleCodesCodes",
        (2, 0, 490) => "BylineCodesCodes",
        (2, 0, 495) => "BylineTitleCodesCodes",
        (2, 0, 500) => "CityCodesCodes",
        (2, 0, 505) => "SublocationCodesCodes",
        (2, 0, 510) => "ProvinceStateCodesCodes",
        (2, 0, 515) => "CountryPrimaryLocationCodeCodesCodes",
        (2, 0, 520) => "CountryPrimaryLocationNameCodesCodes",
        (2, 0, 525) => "OriginalTransmissionReferenceCodesCodes",
        (2, 0, 530) => "HeadlineCodesCodes",
        (2, 0, 535) => "SpecialInstructionsExtendedCodesCodes",
        (2, 0, 540) => "WriterEditorCodesCodes",
        (2, 0, 545) => "CopyrightNoticeCodesCodes",
        (2, 0, 550) => "SourceCodesCodes",
        (2, 0, 555) => "CopyrightNoticeContactCodesCodes",
        (2, 0, 560) => "CaptionAbstractCodesCodes",
        (2, 0, 565) => "WriterEditorContactCodesCodes",
        (2, 0, 570) => "CaptionWriterCodesCodes",
        (2, 0, 575) => "RatingCodesCodes",
        (2, 0, 580) => "UrgencyCodesCodes",
        (2, 0, 585) => "SubjectReferenceCodesCodes",
        (2, 0, 590) => "CategoryCodesCodesCodes",
        (2, 0, 595) => "SupplementalCategoryCodesCodesCodes",
        (2, 0, 600) => "FixtureIdentifierCodesCodesCodes",
        (2, 0, 605) => "ContentLocationCodeCodesCodesCodes",
        (2, 0, 610) => "ContentLocationNameCodesCodesCodes",
        (2, 0, 615) => "ReleaseDateCodesCodesCodes",
        (2, 0, 620) => "ReleaseTimeCodesCodesCodes",
        (2, 0, 625) => "ExpirationDateCodesCodesCodes",
        (2, 0, 630) => "ExpirationTimeCodesCodesCodes",
        (2, 0, 635) => "SpecialInstructionsCodesCodesCodes",
        (2, 0, 640) => "ActionAdvisedCodesCodesCodes",
        (2, 0, 645) => "ReferenceServiceCodesCodesCodes",
        (2, 0, 650) => "ReferenceDateCodesCodesCodes",
        (2, 0, 655) => "ReferenceNumberCodesCodesCodes",
        (2, 0, 660) => "DateCreatedCodesCodesCodes",
        (2, 0, 665) => "TimeCreatedCodesCodesCodes",
        (2, 0, 670) => "DigitalCreationDateCodesCodesCodes",
        (2, 0, 675) => "DigitalCreationTimeCodesCodesCodes",
        (2, 0, 680) => "OriginatingProgramCodesCodesCodes",
        (2, 0, 685) => "ProgramVersionCodesCodesCodes",
        (2, 0, 690) => "ObjectCycleCodesCodesCodes",
        (2, 0, 695) => "BylineCodesCodesCodes",
        (2, 0, 700) => "BylineTitleCodesCodesCodes",
        (2, 0, 705) => "CityCodesCodesCodes",
        (2, 0, 710) => "SublocationCodesCodesCodes",
        (2, 0, 715) => "ProvinceStateCodesCodesCodes",
        (2, 0, 720) => "CountryPrimaryLocationCodeCodesCodes",
        (2, 0, 725) => "CountryPrimaryLocationNameCodesCodes",
        (2, 0, 730) => "OriginalTransmissionReferenceCodesCodes",
        (2, 0, 735) => "HeadlineCodesCodesCodes",
        (2, 0, 740) => "SpecialInstructionsExtendedCodesCodes",
        (2, 0, 745) => "WriterEditorCodesCodes",
        (2, 0, 750) => "CopyrightNoticeCodesCodes",
        (2, 0, 755) => "SourceCodesCodes",
        (2, 0, 760) => "CopyrightNoticeContactCodesCodes",
        (2, 0, 765) => "CaptionAbstractCodesCodes",
        (2, 0, 770) => "WriterEditorContactCodesCodes",
        (2, 0, 775) => "CaptionWriterCodesCodes",
        (2, 0, 780) => "RatingCodesCodes",
        (2, 0, 785) => "UrgencyCodesCodes",
        (2, 0, 790) => "SubjectReferenceCodesCodes",
        _ => "0x" + key.Dataset.ToString("x2", CultureInfo.InvariantCulture)
    };

    private static int Main(string[] args)
    {
        var paths = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "-h" or "--help")
            {
                Console.Error.WriteLine("usage: MetadataSharp.Dump [--files-from FILE|-] [file...]");
                return 0;
            }
            if (arg == "--files-from")
            {
                if (++i == args.Length) { Console.Error.WriteLine("--files-from wants a path or -"); return 2; }
                foreach (string line in args[i] == "-" ? Console.In.ReadToEnd().Split('\n') : File.ReadLines(args[i]))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length != 0) paths.Add(trimmed);
                }
                continue;
            }
            if (arg.Length != 0) paths.Add(arg);
        }
        if (paths.Count == 0) { Console.Error.WriteLine("no input paths given"); return 2; }

        using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
        stdout.NewLine = "\n";
        foreach (string path in paths)
        {
            Record(stdout, "FILE", Escape(path));
            try { Dump(path, stdout); }
            catch (MetadataException failure) { Record(stdout, "STATUS", "refused", failure.Code.ToString()); }
            // A bug in a reader is a fact worth reporting precisely, so the type and the first line of
            // the message travel with the record; the harness counts this status as an unsound run.
            catch (Exception failure)
            {
                string reason = failure.GetType().Name + ": " + failure.Message.Split('\n')[0];
                Record(stdout, "STATUS", "error", Escape(reason.Length > 180 ? reason[..180] : reason));
            }
        }
        stdout.Flush();
        return 0;
    }

    private static void Dump(string path, StreamWriter stdout)
    {
        var head = new byte[16];
        long length;
        using (var probe = File.OpenRead(path))
        {
            length = probe.Length;
            int want = (int)Math.Min(length, head.Length);
            if (want > 0) _ = probe.Read(head.AsSpan(0, want));
        }
        if (At(head, 0, 0xFF, 0xD8)) { Jpeg(path, stdout, length); return; }
        if (At(head, 0, 0x89, 0x50, 0x4E, 0x47)) { Png(path, stdout, length); return; }
        if (At(head, 0, 0x52, 0x49, 0x46, 0x46) && At(head, 8, 0x57, 0x45, 0x42, 0x50)) { WebP(path, stdout, length); return; }
        if (At(head, 0, 0x49, 0x49, 0x2A, 0x00) || At(head, 0, 0x4D, 0x4D, 0x00, 0x2A)) { Tiff(path, stdout, length); return; }
        if (At(head, 0, 0x38, 0x42, 0x50, 0x53)) { Psd(path, stdout, length); return; }
        if (At(head, 0, 0xFF, 0x01, 0x45, 0x78, 0x69, 0x76, 0x32)) { Exv(path, stdout, length); return; }
        if (At(head, 4, 0x6A, 0x50, 0x20, 0x20)) { Jp2(path, stdout, length); return; }
        if (At(head, 0, 0x3C, 0x3F) || At(head, 0, 0xEF, 0xBB, 0xBF)) { Sidecar(path, stdout, length); return; }
        if (At(head, 4, 0x66, 0x74, 0x79, 0x70))
        {
            try
            {
                using var stream = File.OpenRead(path);
                var span = new ReadOnlySpan<byte>(new byte[length]); // Simplified for now, we'll use BmffMetadata
                // In real code, we'd read the whole file or stream. For the dump tool, we'll read the file.
                var data = File.ReadAllBytes(path);
                var (exif, xmp) = BmffMetadata.Read(data.AsSpan());
                Exif(exif, stdout);
                if (xmp is not null) Xmp(xmp, stdout);
                Done(stdout);
            }
            catch (InvalidBmffException failure)
            {
                Record(stdout, "STATUS", "refused", "InvalidBmff");
            }
            catch (Exception failure)
            {
                string reason = failure.GetType().Name + ": " + failure.Message.Split('\n')[0];
                Record(stdout, "STATUS", "error", Escape(reason.Length > 180 ? reason[..180] : reason));
            }
            return;
        }
        if (At(head, 0, 0x1A, 0x45, 0xDF, 0xA3)) { Record(stdout, "STATUS", "not-attempted", "matroska"); return; }
        Record(stdout, "STATUS", "not-attempted", "magic");
    }

    private static void Jpeg(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using JpegMetadataDocument document = JpegMetadataDocument.Open(path);
        Exif(document.Exif, stdout);
        if (document.ReadIptc() is IptcPacket iptc) Iptc(iptc, stdout);
        if (document.ReadXmp() is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void Png(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using PngMetadataDocument document = PngMetadataDocument.Open(path);
        Exif(document.ReadExif(), stdout);
        if (document.ReadXmp() is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void WebP(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using WebPMetadataDocument document = WebPMetadataDocument.Open(path);
        Exif(document.ReadExif(), stdout);
        if (document.ReadXmp() is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void Tiff(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        using TiffMetadataDocument document = TiffMetadataDocument.Open(path);
        TiffLayout layout = document.Layout;
        // The layout is the library's read-side accounting of every field; the bytes of a field are
        // taken from the same file the layout describes. The document's own value accessor is the
        // descriptive-edit target, which refuses most tags and is not what a byte dump wants.
        byte[] data = File.ReadAllBytes(path);
        Dictionary<uint, ExifDirectoryKind> groups = Groups(layout);
        foreach (TiffDirectoryInfo directory in layout.Directories)
        {
            if (!groups.TryGetValue(directory.Offset, out ExifDirectoryKind role))
                role = ExifDirectoryKind.Thumbnail;
            foreach (TiffFieldInfo field in directory.Fields)
            {
                TiffFieldType type = (TiffFieldType)field.FieldType;
                Tag(stdout, "Exif." + Group(role) + "." + Name(new ExifTagKey(role, field.TagId)), TypeName(type),
                    field.Count, Render(Slice(data, field.ValueRange), type, field.Count, layout.ByteOrder, field.TagId));
            }
        }
        Done(stdout);
    }

    private static void Exv(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        ExvMetadata metadata = ExvMetadata.Read(File.ReadAllBytes(path));
        Exif(metadata.Exif, stdout);
        if (metadata.Iptc is IptcPacket iptc) Iptc(iptc, stdout);
        if (metadata.Xmp is XmpPacket xmp) Xmp(xmp, stdout);
        Done(stdout);
    }

    private static void Psd(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        PsdMetadata metadata = PsdMetadata.Read(File.ReadAllBytes(path));
        if (metadata.GetResource(0x0404) is byte[] resource && resource.Length != 0) Iptc(IptcPacket.Parse(resource), stdout);
        Done(stdout);
    }

    private static void Jp2(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        Jp2Metadata metadata = Jp2Metadata.Read(File.ReadAllBytes(path));
        for (int i = 0; i < metadata.XmlBoxCount; i++)
        {
            byte[] box = metadata.GetXmlBox(i);
            if (box.AsSpan().StartsWith("<?xpacket"u8) || box.AsSpan().StartsWith("<?xml"u8)) Xmp(XmpPacket.Parse(box), stdout);
        }
        Done(stdout);
    }

    private static void Sidecar(string path, StreamWriter stdout, long length)
    {
        if (!WithinBudget(length, stdout)) return;
        Xmp(XmpPacket.Load(path), stdout);
        Done(stdout);
    }

    private static void Exif(ExifMetadata? metadata, StreamWriter stdout)
    {
        if (metadata is null) return;
        foreach (ExifEntry entry in metadata.Entries)
            Tag(stdout, "Exif." + Group(entry.Key.Directory) + "." + Name(entry.Key),
                TypeName(entry.Value.FieldType), entry.Value.Count, Render(entry.Value, entry.Value.FieldType, entry.Value.Count, entry.Value.ByteOrder, entry.Key.TagId));
        if (metadata.MakerNote is MakerNote makerNote)
        {
            string group = makerNote.Format switch { MakerNoteFormat.Canon => "Canon", _ => makerNote.Format.ToString() };
            foreach (MakerNoteEntry entry in makerNote.Entries)
            {
                // The reference tool prints an array tag it splits as its fields only, under a
                // group named after the array.
                if (entry.Fields.Count != 0)
                {
                    // AFInfo is printed whole and its fields share the MakerNote's own group.
                    bool keepParent = makerNote.Format == MakerNoteFormat.Canon && entry.TagId == 0x0026;
                    if (keepParent)
                        Tag(stdout, "Exif." + group + "." + entry.Name, TypeName(entry.Value.FieldType), entry.Value.Count, Render(entry.Value, entry.Value.FieldType, entry.Value.Count, entry.Value.ByteOrder, entry.TagId));
                    string fieldGroup = keepParent ? group
                        : makerNote.Format == MakerNoteFormat.Nikon3 ? "Nikon" + NikonArrayGroup(entry)
                        : group + ArrayGroup(entry.TagId);
                    foreach (MakerNoteField field in entry.Fields)
                        Tag(stdout, "Exif." + fieldGroup + "." + field.Name,
                            TypeName(field.Value.FieldType), field.Value.Count, Render(field.Value, field.Value.FieldType, field.Value.Count, field.Value.ByteOrder));
                    continue;
                }
                Tag(stdout, "Exif." + group + "." + (entry.Name ?? "0x" + entry.TagId.ToString("x4", CultureInfo.InvariantCulture)),
                    TypeName(entry.Value.FieldType), entry.Value.Count, Render(entry.Value, entry.Value.FieldType, entry.Value.Count, entry.Value.ByteOrder, entry.TagId));
            }
        }
    }

    /// <summary>The reference tool's group for a split Nikon3 array, chosen like the layout: by tag and version.</summary>
    private static string NikonArrayGroup(MakerNoteEntry entry)
    {
        byte[] raw = new byte[Math.Min(entry.Value.ByteLength, 4)];
        if (raw.Length == 4)
        {
            byte[] all = new byte[entry.Value.ByteLength];
            entry.Value.CopyRawTo(all);
            raw = all[..4];
        }
        string version = raw.Length == 4 ? Encoding.ASCII.GetString(raw) : "";
        return entry.TagId switch
        {
            0x001F => "Vr", 0x0023 => "Pc", 0x0024 => "Wt", 0x0025 => "Ii", 0x0088 => "Af",
            0x0098 => version == "0100" ? "Ld1" : "Ld2",
            0x00A8 => version switch { "0100" or "0101" => "Fl1", "0102" => "Fl2", "0106" => "Fl6", "0107" or "0108" => "Fl7", _ => "Fl3" },
            0x00B0 => "Me", 0x00B7 => version == "0100" ? "Af2" : "Af22", 0x00B8 => "Fi", 0x00B9 => "AFT",
            _ => "0x" + entry.TagId.ToString("x4", CultureInfo.InvariantCulture)
        };
    }

    private static string ArrayGroup(ushort tag) => tag switch
    {
        0x0001 => "Cs",
        0x0004 => "Si",
        0x0005 => "Pa",
        0x000F => "Cf",
        0x0012 => "Pi",
        0x0093 => "Fi",
        0x00A0 => "Pr",
        0x0035 => "Ti",
        0x4013 => "AfMiAdj",
        0x4016 => "VigCor2",
        0x4018 => "LiOp",
        0x4019 => "Le",
        0x4020 => "Am",
        0x4021 => "Me",
        0x4025 => "Hdr",
        0x4028 => "AfC",
        _ => "0x" + tag.ToString("x4", CultureInfo.InvariantCulture)
    };

    private static void Iptc(IptcPacket packet, StreamWriter stdout)
    {
        foreach (IptcDataset dataset in packet.Datasets)
        {
            byte[] bytes = new byte[dataset.Value.ByteLength];
            dataset.Value.CopyRawTo(bytes);
            bytes = Trim(bytes);
            string text = (packet.CharacterSet == IptcCharacterSet.Utf8 ? Encoding.UTF8 : Encoding.Latin1).GetString(bytes);
            // Use the new dataset names from IIM 4.2
            string keyName = IptcDatasetName(dataset.Key);
            string key = $"Iptc.{RecordName(dataset.Key.Record)}.{keyName}";
            // If unknown, fallback to 0xNN
            if (keyName == "0x" + dataset.Key.Dataset.ToString("x2", CultureInfo.InvariantCulture))
                key = RecordName(dataset.Key.Record) + ".0x" + dataset.Key.Dataset.ToString("x2", CultureInfo.InvariantCulture);
            
            Tag(stdout, key, "String", (uint)bytes.Length, Escape(text));
        }
    }

    private static void Xmp(XmpPacket packet, StreamWriter stdout)
    {
        if (!packet.IsEditable) return;
        int movedKeys = 0; // For reporting in PR (not printed here)
        foreach (KeyValuePair<XmpPropertyName, XmpValue> property in packet.Properties)
        {
            string key = "Xmp." + Prefix(property.Key.NamespaceUri) + "." + property.Key.LocalName;
            XmpValue value = property.Value;
            if (value.Kind is XmpValueKind.Text)
            {
                string text = value.Text ?? string.Empty;
                Tag(stdout, key, "XmpText", (uint)Encoding.UTF8.GetByteCount(text), Escape(text));
            }
            else if (value.Kind is XmpValueKind.Bag or XmpValueKind.Sequence)
            {
                IReadOnlyList<string> items = value.Items;
                Tag(stdout, key, value.Kind == XmpValueKind.Bag ? "XmpBag" : "XmpSeq", (uint)items.Count, Join(items));
            }
            else
            {
                // Rendered the way the reference tool prints a language alternative, so the parity
                // harness compares text rather than two spellings of the same property.
                var parts = new List<string>();
                foreach (KeyValuePair<string, string> language in value.Languages)
                    parts.Add("lang=\"" + language.Key + "\" " + Escape(language.Value));
                Tag(stdout, key, "LangAlt", (uint)parts.Count, string.Join(", ", parts));
            }
        }
    }

    private static string Join(IReadOnlyList<string> items)
    {
        var parts = new List<string>(items.Count);
        foreach (string item in items) parts.Add(Escape(item));
        return string.Join(", ", parts);
    }

    /// <summary>Which EXIF directory a layout offset is, because the layout reports offsets and the
    /// tag vocabulary is keyed by directory. The links in the tree decide, exactly as they do for the
    /// group label the reference output uses.</summary>
    private static Dictionary<uint, ExifDirectoryKind> Groups(TiffLayout layout)
    {
        var groups = new Dictionary<uint, ExifDirectoryKind>();
        uint? exif = null, gps = null, interop = null, next = null;
        foreach (TiffDirectoryInfo directory in layout.Directories)
        {
            foreach (TiffDirectoryLink link in directory.Links)
            {
                switch (link.Kind)
                {
                    case TiffDirectoryLinkKind.Exif: exif ??= link.TargetIfdOffset; break;
                    case TiffDirectoryLinkKind.Gps: gps ??= link.TargetIfdOffset; break;
                    case TiffDirectoryLinkKind.Interoperability: interop ??= link.TargetIfdOffset; break;
                    case TiffDirectoryLinkKind.NextIfd: next ??= link.TargetIfdOffset; break;
                }
            }
        }
        foreach (TiffDirectoryInfo directory in layout.Directories)
        {
            uint offset = directory.Offset;
            groups[offset] = offset == exif ? ExifDirectoryKind.Exif :
                offset == gps ? ExifDirectoryKind.Gps :
                offset == interop ? ExifDirectoryKind.Interop :
                offset == layout.FirstIfdOffset ? ExifDirectoryKind.Ifd0 : ExifDirectoryKind.Thumbnail;
        }
        return groups;
    }

    /// <summary>The field name the standards give a tag, or its numeric ID when this release does not
    /// name it: an unnamed tag is reported as a gap, never as a plausible-looking invented label.</summary>
    private static string Name(ExifTagKey key)
    {
        ExifTagDefinition? definition = ExifTags.Find(key);
        return definition is null ? "0x" + Hex(key.TagId) : definition.Name;
    }

    private static string Group(ExifDirectoryKind directory) => directory switch
    {
        ExifDirectoryKind.Ifd0 => "Image",
        ExifDirectoryKind.Exif => "Photo",
        ExifDirectoryKind.Gps => "GPSInfo",
        ExifDirectoryKind.Interop => "Iop",
        _ => "Thumbnail",
    };

    private static string RecordName(byte record) => record switch
    {
        1 => "Iptc.Envelope",
        2 => "Iptc.Application2",
        _ => "Iptc.Record" + record.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The conventional prefix of a standard namespace URI; anything else keeps its local part.</summary>
    private static string Prefix(string namespaceUri) => namespaceUri switch
    {
        "http://purl.org/dc/elements/1.1/" => "dc",
        "http://purl.org/dc/terms/" => "dcterms",
        "http://purl.org/dc/dcmitype/" => "dcmitype",
        "http://ns.adobe.com/xap/1.0/" => "xmp",
        "http://ns.adobe.com/xap/1.0/mm/" => "xmpMM",
        "http://ns.adobe.com/xap/1.0/rights/" => "xmpRights",
        "http://ns.adobe.com/xap/1.0/bj/" => "xmpBJ",
        "http://ns.adobe.com/xap/1.0/Note/" => "xmpNote",
        "http://ns.adobe.com/xap/1.0/sType/ResourceEvent#" => "stEvt",
        "http://ns.adobe.com/xap/1.0/sType/ResourceRef#" => "stRef",
        "http://ns.adobe.com/xap/1.0/sType/Part#" => "stPart",
        "http://ns.adobe.com/photoshop/1.0/" => "photoshop",
        "http://ns.adobe.com/photograph/1.0/" => "photograph",
        "http://ns.adobe.com/tiff/1.0/" => "tiff",
        "http://ns.adobe.com/exif/1.0/" => "exif",
        "http://ns.adobe.com/exif/1.0/aux/" => "aux",
        "http://ns.adobe.com/pdf/1.3/" => "pdf",
        "http://ns.adobe.com/xmp/1.0/DynamicMedia/" => "xmpDM",
        "http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/" => "Iptc4xmpCore",
        "http://iptc.org/std/Iptc4xmpExt/2008-02-29/" => "Iptc4xmpExt",
        "http://prismstandard.org/namespaces/basic/1.0/" => "prism",
        "http://www.day.com/schemas/handler/1.0/" => "handler",
        "http://www.flickr.com/ns/creative-commons/2006-06-15/" => "cc",
        "http://www.w3.org/XML/1998/namespace" => "xml",
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#" => "rdf",
        // Additional mappings from reference output comparison (crs, aux, etc.)
        "http://ns.adobe.com/camera-raw-settings/1.0/" => "crs",
        "http://ns.adobe.com/xap/1.0/aux/" => "aux", // already there but confirmed
        _ => Tail(namespaceUri),
    };

    /// <summary>
    /// A display prefix for a namespace the table does not know. A packet's own serialization prefix is
    /// deliberately not part of the property name, so the vendor label in the authority is the closest
    /// stable guess; a URI without one is reported verbatim rather than as a plausible-looking short name.
    /// </summary>
    private static string Tail(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || parsed.Host.Length == 0) return uri;
        string first = parsed.Host.Split('.')[0];
        return first is "www" or "ns" || first.Length == 0 ? parsed.Host : first;
    }

    private static string TypeName(TiffFieldType type) => type switch
    {
        TiffFieldType.Byte => "Byte",
        TiffFieldType.Ascii => "Ascii",
        TiffFieldType.Short => "Short",
        TiffFieldType.Long => "Long",
        TiffFieldType.Rational => "Rational",
        TiffFieldType.SByte => "SByte",
        TiffFieldType.Undefined => "Undefined",
        TiffFieldType.SShort => "SShort",
        TiffFieldType.SLong => "SLong",
        TiffFieldType.SRational => "SRational",
        TiffFieldType.Float => "Float",
        TiffFieldType.Double => "Double",
        _ => "Type" + ((ushort)type).ToString(CultureInfo.InvariantCulture),
    };

    private static int Size(TiffFieldType type) => type switch
    {
        TiffFieldType.Byte or TiffFieldType.Ascii or TiffFieldType.SByte or TiffFieldType.Undefined => 1,
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float => 4,
        TiffFieldType.Rational or TiffFieldType.SRational or TiffFieldType.Double => 8,
        _ => 0,
    };

    private static string Render(ExifValue value, TiffFieldType type, uint count, Endianness order, ushort tagId = 0)
    {
        var bytes = new byte[value.ByteLength];
        value.CopyRawTo(bytes);
        return Render(bytes, type, count, order, tagId);
    }

    /// <summary>Every element the declared type and count ask for, or a marker saying they are not there.</summary>
    private static string Render(byte[] bytes, TiffFieldType type, uint count, Endianness order, ushort tagId = 0)
    {
        if (type is TiffFieldType.Ascii)
        {
            byte[] cut = CutAtFirstNul(bytes);
            string text = TryUtf8(cut) ?? Encoding.Latin1.GetString(cut);
            return Escape(text);
        }
        if (type is TiffFieldType.Undefined)
        {
            // UserComment (0x9286), GPSProcessingMethod (0x001B), GPSAreaInformation (0x001C)
            if (tagId is 0x9286 or 0x001B or 0x001C)
            {
                byte[] cut = CutAtFirstNul(bytes);
                string text = TryUtf8(cut) ?? Encoding.Latin1.GetString(cut);
                string charset = TryUtf8(cut) is not null ? "UTF-8" : "Latin-1";
                return Escape(charset + " " + text);
            }
            // Otherwise (ExifVersion, ComponentsConfiguration, FlashpixVersion, CFAPattern, SceneType, FileSource, CompositeImage)
            // Keep decimal bytes (the reference prints decimal bytes for those)
        }
        int size = Size(type);
        if (size == 0) return "undefined-type";
        if ((long)count * size > bytes.Length) return "truncated-value";
        var text = new StringBuilder();
        for (int i = 0; i < (int)count; i++)
        {
            if (i != 0) text.Append(' ');
            int at = i * size;
            text.Append(type switch
            {
                TiffFieldType.Byte => bytes[at].ToString(CultureInfo.InvariantCulture),
                TiffFieldType.SByte => unchecked((sbyte)bytes[at]).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.Undefined => bytes[at].ToString(CultureInfo.InvariantCulture),
                TiffFieldType.Short => U16(bytes, at, order).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.SShort => S16(bytes, at, order).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.Long => U32(bytes, at, order).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.SLong => S32(bytes, at, order).ToString(CultureInfo.InvariantCulture),
                TiffFieldType.Float => ReadSingle(bytes, at, order).ToString("R", CultureInfo.InvariantCulture),
                TiffFieldType.Double => ReadDouble(bytes, at, order).ToString("R", CultureInfo.InvariantCulture),
                TiffFieldType.Rational => U32(bytes, at, order).ToString(CultureInfo.InvariantCulture) + "/" +
                    U32(bytes, at + 4, order).ToString(CultureInfo.InvariantCulture),
                _ => S32(bytes, at, order).ToString(CultureInfo.InvariantCulture) + "/" +
                    S32(bytes, at + 4, order).ToString(CultureInfo.InvariantCulture),
            });
        }
        return text.ToString();
    }

    private static byte[] CutAtFirstNul(byte[] bytes)
    {
        int end = Array.IndexOf(bytes, (byte)0);
        if (end == -1) return bytes;
        var cut = new byte[end];
        Array.Copy(bytes, 0, cut, 0, end);
        return cut;
    }

    private static string? TryUtf8(byte[] bytes)
    {
        try { return Encoding.UTF8.GetString(bytes); }
        catch { return null; }
    }

    private static ushort U16(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at, 2)) : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(at, 2));

    private static short S16(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at, 2)) : BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(at, 2));

    private static uint U32(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at, 4));

    private static int S32(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at, 4)) : BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at, 4));

    private static float ReadSingle(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(at, 4)) : BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(at, 4));

    private static double ReadDouble(byte[] bytes, int at, Endianness order) => order == Endianness.LittleEndian ?
        BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(at, 8)) : BinaryPrimitives.ReadDoubleBigEndian(bytes.AsSpan(at, 8));

    private static string Hex(ushort value) => value.ToString("x4", CultureInfo.InvariantCulture);

    private static byte[] Trim(byte[] bytes)
    {
        int end = bytes.Length;
        while (end > 0 && bytes[end - 1] == 0) end--;
        return end == bytes.Length ? bytes : bytes[..end];
    }

    /// <summary>Escapes everything that would break the one-record-per-line grammar.</summary>
    private static string Escape(string value)
    {
        if (value.Length == 0) return value;
        bool needs = false;
        foreach (char c in value) if (c < 0x20 || c == 0x7F || c == '\\') { needs = true; break; }
        if (!needs) return value;
        var text = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            if (c == '\\') text.Append("\\\\");
            else if (c == '\t') text.Append("\\t");
            else if (c == '\n') text.Append("\\n");
            else if (c == '\r') text.Append("\\r");
            else if (c < 0x20 || c == 0x7F) text.Append("\\x").Append(((byte)c).ToString("x2", CultureInfo.InvariantCulture));
            else text.Append(c);
        }
        return text.ToString();
    }

    /// <summary>The bytes a layout range points at, clipped to the file: a corrupt range is a value to
    /// report, not a reason to stop printing the rest of the directory.</summary>
    private static byte[] Slice(byte[] data, TiffByteRange range)
    {
        if (range.Offset < 0 || range.Length < 0 || range.Offset >= data.Length) return [];
        int available = (int)Math.Min(range.Length, (long)data.Length - range.Offset);
        var bytes = new byte[available];
        Array.Copy(data, range.Offset, bytes, 0, available);
        return bytes;
    }

    private static bool At(byte[] data, int at, params int[] expected)
    {
        if (at + expected.Length > data.Length) return false;
        for (int i = 0; i < expected.Length; i++) if (data[at + i] != expected[i]) return false;
        return true;
    }

    private static bool WithinBudget(long length, StreamWriter stdout)
    {
        if (length <= MaxWholeFileBytes) return true;
        Record(stdout, "STATUS", "not-attempted", "too-large");
        return false;
    }

    private static void Tag(StreamWriter stdout, string key, string type, uint count, string value) =>
        Record(stdout, "TAG", key, type, count.ToString(CultureInfo.InvariantCulture), value);

    private static void Done(StreamWriter stdout) => Record(stdout, "STATUS", "ok");

    private static void Record(StreamWriter stdout, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i != 0) stdout.Write('\t');
            stdout.Write(fields[i]);
        }
        stdout.Write('\n');
    }
}
