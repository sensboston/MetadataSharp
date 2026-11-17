namespace MetadataSharp.Harness.Tests;

// The test assembly's own entry point (GenerateProgramFile is off). The test host never calls it: the tests of the
// fuzz runner start this assembly as a child process, with "fuzz-stand-in", for a harness whose inputs' names say
// what it does with them.
public static class Program
{
    public static int Main(string[] args) => args.Length > 0 && args[0] == Fuzz.FuzzStandIn.Command ? Fuzz.FuzzStandIn.Run(args[1..]) : 0;
}
