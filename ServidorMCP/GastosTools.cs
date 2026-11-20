using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace ServidorMCP;

[McpServerToolType]
public static class GastosTools
{
    public static string RutaArchivo { get; set; } = "gastos.json";

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        WriteIndented = true
    };

    private static List<Gasto> LeerGastos()
    {
        if (!File.Exists(RutaArchivo))
            return new List<Gasto>();

        string contenido = File.ReadAllText(RutaArchivo);
        if (string.IsNullOrWhiteSpace(contenido))
            return new List<Gasto>();

        return JsonSerializer.Deserialize<List<Gasto>>(contenido) ?? new List<Gasto>();
    }

    private static void GuardarGastos(List<Gasto> gastos)
    {
        string json = JsonSerializer.Serialize(gastos, OpcionesJson);
        File.WriteAllText(RutaArchivo, json);
    }

    [McpServerTool, Description("Registra un nuevo gasto en el archivo gastos.json.")]
    public static object RegistrarGasto(
        [Description("En qué se gastó el dinero, por ejemplo: Tacos, Gasolina, Café")] string concepto,
        [Description("Cantidad de dinero gastada. Debe ser mayor a 0")] decimal monto,
        [Description("Categoría del gasto, por ejemplo: Comida, Transporte, Servicios, Otros")] string categoria = "General")
    {
        if (string.IsNullOrWhiteSpace(concepto))
            return new { ok = false, error = "El concepto del gasto es obligatorio." };

        if (monto <= 0)
            return new { ok = false, error = "El monto debe ser mayor a 0." };

        var gastos = LeerGastos();

        var nuevoGasto = new Gasto
        {
            Id = Guid.NewGuid().ToString("N")[..4].ToUpper(),
            Concepto = concepto.Trim(),
            Monto = monto,
            Categoria = string.IsNullOrWhiteSpace(categoria) ? "General" : categoria.Trim(),
            Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };

        gastos.Add(nuevoGasto);
        GuardarGastos(gastos);

        return new
        {
            ok = true,
            mensaje = "Gasto registrado correctamente.",
            gasto = nuevoGasto
        };
    }

    [McpServerTool, Description("Consulta los gastos registrados y calcula el total. Puede filtrar por categoría si se indica.")]
    public static object ConsultarGastos(
        [Description("Categoría opcional para filtrar (ejemplo: Comida). Déjalo vacío para ver todos los gastos.")] string? categoria = null)
    {
        var gastos = LeerGastos();

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            gastos = gastos
                .Where(g => g.Categoria.Equals(categoria.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        decimal total = gastos.Sum(g => g.Monto);

        return new
        {
            ok = true,
            cantidad = gastos.Count,
            total,
            gastos
        };
    }

    [McpServerTool, Description("Elimina un gasto existente usando su identificador (ID).")]
    public static object EliminarGasto(
        [Description("Identificador (ID) del gasto que se desea eliminar, por ejemplo: A1B2")] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return new { ok = false, error = "Debes indicar el ID del gasto a eliminar." };

        var gastos = LeerGastos();
        var gastoEncontrado = gastos.FirstOrDefault(g => g.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

        if (gastoEncontrado == null) {
            return new { ok = false, error = $"No existe ningún gasto con el ID '{id}'." };
        }
            
        gastos.Remove(gastoEncontrado);
        GuardarGastos(gastos);

        return new
        {
            ok = true,
            mensaje = $"Gasto '{gastoEncontrado.Concepto}' eliminado correctamente.",
            gastoEliminado = gastoEncontrado
        };
    }
}