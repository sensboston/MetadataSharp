using MetaTool.Cli;

// The whole command line lives in Cli/: an argument grammar, the nine actions of the Exiv2 command
// line and the renderers of its reports, all on the public API of MetadataSharp.
return Runner.Run(args, Console.Out, Console.Error, Console.OpenStandardInput(), Console.OpenStandardOutput());
