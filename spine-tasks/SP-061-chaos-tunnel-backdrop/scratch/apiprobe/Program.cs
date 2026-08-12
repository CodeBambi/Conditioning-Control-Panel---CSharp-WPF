var wd = typeof(Avalonia.Controls.Window).Assembly.GetType("Avalonia.Controls.WindowDecorations");
Console.WriteLine("WindowDecorations: " + (wd is null ? "ABSENT" : string.Join(",", Enum.GetNames(wd))));
var w = typeof(Avalonia.Controls.Window);
var prop = w.GetProperties().FirstOrDefault(p => p.PropertyType == wd);
Console.WriteLine("property: " + (prop?.Name ?? "NONE on Window") );
foreach (var p in w.GetProperties().Where(p => p.PropertyType.Name.Contains("Decorations"))) Console.WriteLine($"Window.{p.Name} : {p.PropertyType.Name}");
