namespace ServidorMCP;

public class Gasto
{
    public string Id { get; set; } = "";
    public string Concepto { get; set; } = "";
    public decimal Monto { get; set; }
    public string Categoria { get; set; } = "";
    public string Fecha { get; set; } = "";
}