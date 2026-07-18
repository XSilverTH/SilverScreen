using Adw;
using SilverScreen;
using XSTH.Blueprint.Helpers;

Module.Initialize();
WebKit.Module.Initialize();
GResourceHelper.RegisterAssemblyResources(typeof(Program).Assembly);

var app = App.NewWithProperties([]);
return app.RunWithSynchronizationContext(args);
