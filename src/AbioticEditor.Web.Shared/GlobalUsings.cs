// Several of the moved services log through ILogger. They got that type from the ASP.NET Core
// shared framework when they lived in the desktop host; a plain Razor Class Library does not
// reference it, so the using is declared once here rather than added to each file.
//
// The routing/RenderMode usings the desktop host also declares globally are NOT repeated here:
// only .razor files need them, and _Imports.razor already covers those.
global using Microsoft.Extensions.Logging;
