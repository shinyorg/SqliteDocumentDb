namespace Shiny.SqliteDocumentDb.Tests.Fixtures;

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

public class UserSummary
{
    public string Name { get; set; } = "";
    public string? Email { get; set; }
}

public class OrderSummary
{
    public string Customer { get; set; } = "";
    public string City { get; set; } = "";
}

public class OrderDetail
{
    public string Customer { get; set; } = "";
    public int LineCount { get; set; }
    public bool HasPriority { get; set; }
}

public class OrderLineAggregates
{
    public string Customer { get; set; } = "";
    public int TotalQty { get; set; }
    public decimal MaxPrice { get; set; }
    public decimal MinPrice { get; set; }
    public double AvgPrice { get; set; }
}

public class OrderStats
{
    public string Status { get; set; } = "";
    public int OrderCount { get; set; }
    public int MaxLineCount { get; set; }
}

public class PriceSummary
{
    public int TotalCount { get; set; }
    public decimal MaxPrice { get; set; }
    public decimal MinPrice { get; set; }
    public decimal SumPrice { get; set; }
    public double AvgPrice { get; set; }
}
