using Arbor.DevPackages.Server.ServiceIndex;
using Arbor.DevPackages.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapServiceIndex();

app.Run();

public partial class Program { }
