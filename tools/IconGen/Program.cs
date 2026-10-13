using AutoDNS;

var path = args.Length > 0 ? args[0] : @"d:\AutoDNS\src\AutoDNS\Assets\app.ico";
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
IconFactory.SaveIco(path, AppConstants.ActiveGreen);
Console.WriteLine("Wrote " + path);
