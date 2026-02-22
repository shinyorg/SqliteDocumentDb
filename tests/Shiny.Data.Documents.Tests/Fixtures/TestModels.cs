namespace Shiny.Data.Documents.Tests.Fixtures;

public class User
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
    public string? Email { get; set; }
}

public class Product
{
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
}

public class Address
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Zip { get; set; } = "";
}

public class Event
{
    public string Title { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class OrderLine
{
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Order
{
    public string CustomerName { get; set; } = "";
    public string Status { get; set; } = "";
    public Address ShippingAddress { get; set; } = new();
    public List<OrderLine> Lines { get; set; } = [];
    public List<string> Tags { get; set; } = [];
}
