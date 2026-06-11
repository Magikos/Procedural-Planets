// Polyfill: records use `init` setters which require IsExternalInit. The type ships in
// .NET 5+ but not in .NET Standard 2.1 (Unity 6's BCL). Declaring it here lets the compiler
// recognize records. Internal so it only satisfies this assembly's records — other
// assemblies that adopt records each need their own copy.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
