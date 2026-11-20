using ServidorMCP;

var builder = WebApplication.CreateBuilder(args);

GastosTools.RutaArchivo = builder.Configuration["ArchivoGastos"] ?? "gastos.json";

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapMcp("/mcp");
app.Run();