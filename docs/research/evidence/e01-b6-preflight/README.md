# B6 DNG exploratory preflight — not census evidence

`probes.json` records a local exploratory run of `tools/reference/probe_b6.py`
from source commit `07a378311e5f2b85022ffd06f5a22a86469ef40b`, against Exiv2
0.28.9 source commit `b0859fc61fea5dedfc898aece6b15893ddf4314e`. Four
deterministic D1 fixtures (raw root and thumbnail-root/SubIFD, II and MM)
were regenerated and checked against their committed manifest. Each probe ran
on a fresh copy. Inventory, metadata read, Artist replacement/removal,
ImageDescription insertion, XMP CreatorTool insertion and IPTC keyword
insertion produced 28 probe results. Commands and subsequent `-pa` readbacks
all returned zero; for the five metadata mutations the requested value appeared
in readback, or the removed Artist key was absent, respectively. The CFA strip
fingerprint was unchanged in all 28 cases. Exact stdout, stderr, commands,
executable hash, build cache settings and input/output hashes are in the JSON.

The oracle compares stored CFA strip bytes, **not decoded or rendered image
content**; raw-decode oracle coverage remains a known blocker-class gap. Zero
exit status and a readback string do not prove general support or safe editing.
In particular this preflight does not cover ICC, all thumbnail operations,
metadata-present replacement/removal for all families, duplicate layouts,
malformed cases, or the full B6 census. It neither modifies the census nor
assesses MetadataSharp; DNG write support remains unauthorized. No denominator
is frozen and no coverage percentage follows from these exploratory probes.

The local executable was built from the pinned source with the required CMake
feature options. Its verbose version records that zlib 1.3.1 and expat 2.6.2
were dynamically linked from the local dependency prefix, while Brotli 1.1.0
and inih r59 were built locally. This is not the CI build, and no CI artifact
or cross-build equivalence is claimed. The report records temporary local file
paths as emitted by Exiv2; these are not fixture identities. The committed
report SHA-256 is
`e51433aa2a21ede5a27fd3acc50fd962353f2d7dcaf9b1614501a00ea2200d08`.
