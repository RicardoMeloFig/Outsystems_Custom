// ISSequenceProbe2 - check ClrArray API available methods
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Diagnostics.Runtime;

var dll = @"C:\Users\ricar\.nuget\packages\microsoft.diagnostics.runtime\3.0.442202\lib\net6.0\Microsoft.Diagnostics.Runtime.dll";
var a = Assembly.LoadFrom(dll);
var t = a.GetType("Microsoft.Diagnostics.Runtime.ClrArray");
Console.WriteLine("=== ClrArray Methods ===");
foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !m.IsSpecialName))
    Console.WriteLine("  " + m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ") -> " + m.ReturnType.Name);
Console.WriteLine("=== ClrArray Properties ===");
foreach (var p in t.GetProperties())
    Console.WriteLine("  " + p.Name + " -> " + p.PropertyType.Name);