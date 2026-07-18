using Adw;
using SilverScreen;
using XSTH.Blueprint.Helpers;

Module.Initialize();
WebKit.Module.Initialize();
GResourceHelper.RegisterAssemblyResources(typeof(Program).Assembly);

var app = new App();
return app.RunWithSynchronizationContext(args);