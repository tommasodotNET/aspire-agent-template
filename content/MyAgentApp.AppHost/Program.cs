var builder = DistributedApplication.CreateBuilder(args);

var agent = builder.AddProject<Projects.MyAgentApp_Agent>("agent")
    .WithUrlForEndpoint("https", url => url.Url = "/devui");

var web = builder.AddProject<Projects.MyAgentApp_Web>("web")
    .WithExternalHttpEndpoints()
    .WithReference(agent)
    .WaitFor(agent);

builder.Build().Run();
