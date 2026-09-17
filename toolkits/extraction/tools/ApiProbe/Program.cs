using System; using System.Linq; using System.Reflection;
var dll=@"C:\Users\ricar\.nuget\packages\microsoft.diagnostics.runtime\3.0.442202\lib\net6.0\Microsoft.Diagnostics.Runtime.dll";
var a=Assembly.LoadFrom(dll);
void Show(string tn){ var t=a.GetType(tn); if(t==null){Console.WriteLine(tn+" : NOT FOUND");return;} Console.WriteLine("=== "+tn+" ==="); foreach(var m in t.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>!m.IsSpecialName&&(m.Name.Contains("Read")||m.Name.Contains("Object")||m.Name.Contains("Array")||m.Name.Contains("Element")||m.Name.Contains("Length")||m.Name.Contains("Count")||m.Name.Contains("Field")||m.Name.Contains("AsArray")||m.Name.Contains("IsArray")))) Console.WriteLine("  M "+m.ReturnType.Name+" "+m.Name+"("+string.Join(",",m.GetParameters().Select(p=>p.ParameterType.Name+" "+p.Name))+")"); foreach(var p in t.GetProperties()) Console.WriteLine("  P "+p.PropertyType.Name+" "+p.Name); }
Show("Microsoft.Diagnostics.Runtime.ClrObject");
Show("Microsoft.Diagnostics.Runtime.ClrType");
Show("Microsoft.Diagnostics.Runtime.ClrArray");
