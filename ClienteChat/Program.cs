using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

// 1. Leer archivo de configuración
if (!File.Exists("config.json"))
{
    Console.WriteLine("No se encontró 'config.json'. Copia 'config.example.json' como 'config.json'.");
    return;
}

var textoConfig = await File.ReadAllTextAsync("config.json");
var config = JsonSerializer.Deserialize<Configuracion>(textoConfig, new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
})!;

string urlLlmCompleta = $"{config.Llm.BaseUrl.TrimEnd('/')}{config.Llm.ChatPath}";

// 2. Mostrar configuración efectiva al iniciar
Console.WriteLine($"Modelo local: {config.Llm.Model}");
Console.WriteLine($"API del LLM:  {urlLlmCompleta}");
Console.WriteLine($"Servidor MCP: {config.Mcp.Url}");
Console.WriteLine();

// 3. Conectarse al servidor MCP y descubrir herramientas
Console.WriteLine("Conectando al servidor MCP...");
var transporte = new HttpClientTransport(new HttpClientTransportOptions {
    Endpoint = new Uri(config.Mcp.Url)
});

await using var mcp = await McpClient.CreateAsync(transporte);
var herramientasMcp = await mcp.ListToolsAsync();

Console.WriteLine("Herramientas MCP descubiertas:");

foreach (var herramienta in herramientasMcp) {
    Console.WriteLine($"- {herramienta.Name}: {herramienta.Description}");
}
Console.WriteLine();

// Convertir las herramientas al formato que entiende Ollama
var herramientasOllama = herramientasMcp.Select(h => new {
    type = "function",
    function = new {
        name = h.Name,
        description = h.Description,
        parameters = h.JsonSchema
    }
}).ToList();

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

// Historial de la conversación
var historial = CrearHistorialInicial();

Console.WriteLine("Chat iniciado. Escribe 'nuevo' para reiniciar la conversación o 'salir' para terminar.");
Console.WriteLine("----------------------------------------------------------------------------------");

while (true)
{
    Console.Write("\nUsuario: ");
    string? entrada = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(entrada))
        continue;

    if (entrada.Trim().Equals("salir", StringComparison.OrdinalIgnoreCase))
        break;

    if (entrada.Trim().Equals("nuevo", StringComparison.OrdinalIgnoreCase))
    {
        historial = CrearHistorialInicial();
        Console.WriteLine("Conversación reiniciada.");
        continue;
    }

    historial.Add(new MensajeOllama
    {
        Role = "user",
        Content = entrada
    });

    // Ciclo: mientras el modelo pida usar herramientas, las ejecutamos y le devolvemos el resultado
    while (true)
    {
        var peticion = new
        {
            model = config.Llm.Model,
            messages = historial,
            stream = false,
            tools = herramientasOllama
        };

        var respuestaHttp = await http.PostAsJsonAsync(urlLlmCompleta, peticion);
        respuestaHttp.EnsureSuccessStatusCode();

        var respuesta = await respuestaHttp.Content.ReadFromJsonAsync<RespuestaOllama>();
        if (respuesta?.Message == null)
            break;

        // Guardamos el mensaje del modelo en el historial
        historial.Add(respuesta.Message);

        // Si el modelo NO pidió herramientas, mostramos su respuesta final y terminamos este turno
        if (respuesta.Message.ToolCalls == null || respuesta.Message.ToolCalls.Count == 0)
        {
            Console.WriteLine($"\nAsistente: {respuesta.Message.Content}");
            break;
        }

        // Si el modelo SÍ pidió herramientas, ejecutamos cada una en el servidor MCP
        foreach (var llamada in respuesta.Message.ToolCalls)
        {
            string nombreHerramienta = llamada.Function.Name;
            var argumentos = llamada.Function.Arguments;

            Console.WriteLine($"\nEl modelo solicita: {nombreHerramienta}");
            Console.WriteLine($"Argumentos: {JsonSerializer.Serialize(argumentos)}");
            Console.WriteLine("Llamando al servidor MCP...");

            var resultadoMcp = await mcp.CallToolAsync(nombreHerramienta, argumentos);
            string textoResultado = resultadoMcp.Content.FirstOrDefault()?.ToString() ?? "Sin resultado.";

            Console.WriteLine($"Resultado MCP: {textoResultado}");

            // Agregamos el resultado de la herramienta al historial para que el modelo lo lea
            historial.Add(new MensajeOllama
            {
                Role = "tool",
                Content = textoResultado
            });
        }
    }
}

static List<MensajeOllama> CrearHistorialInicial() => new()
{
    new MensajeOllama
    {
        Role = "system",
        Content = "Eres un asistente de control de gastos personales. Usa tus herramientas para registrar, consultar o eliminar gastos cuando el usuario lo solicite. Responde siempre en español de manera clara y directa."
    }
};

// --- Clases sencillas para leer configuración y mensajes de Ollama ---

public class Configuracion
{
    public ConfigMcp Mcp { get; set; } = new();
    public ConfigLlm Llm { get; set; } = new();
}

public class ConfigMcp
{
    public string Url { get; set; } = "";
}

public class ConfigLlm
{
    public string Provider { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ChatPath { get; set; } = "";
    public string Model { get; set; } = "";
}

public class RespuestaOllama
{
    [JsonPropertyName("message")]
    public MensajeOllama? Message { get; set; }
}

public class MensajeOllama
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCallOllama>? ToolCalls { get; set; }
}

public class ToolCallOllama
{
    [JsonPropertyName("function")]
    public FuncionOllama Function { get; set; } = new();
}

public class FuncionOllama
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("arguments")]
    public Dictionary<string, object?>? Arguments { get; set; }
}