var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Arbor_DevPackages_Server>("server");

builder.Build().Run();
