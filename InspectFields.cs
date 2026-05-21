using System.Reflection;
// Quick reflection probe — run with `dotnet run`
var asm = Assembly.LoadFrom(
    @"E:\Project\HDOS\tests\Gateway.Tests\bin\Release\net9.0\refs\Microsoft.AspNetCore.RateLimiting.dll");
var t = asm.GetType("Microsoft.AspNetCore.RateLimiting.RateLimiterOptions")!;
Console.WriteLine("=== FIELDS ===");
foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
    Console.WriteLine($"  {f.FieldType.Name} {f.Name}");
Console.WriteLine("=== PROPS ===");
foreach (var p in t.GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
    Console.WriteLine($"  {p.PropertyType.Name} {p.Name}");
