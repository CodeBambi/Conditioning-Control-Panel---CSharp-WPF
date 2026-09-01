// Global using directives
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;

// NOTE: `global using System.Windows;` is deliberately OMITTED here (the app's own
// GlobalUsings.cs has it). That omission is load-bearing: without it, a leaked WPF symbol
// in Core fails to compile instead of silently binding. Do not add it.
