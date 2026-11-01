// netstandard2.0 and net48 compile with <Nullable>annotations</Nullable> (see the project file): their reference
// assemblies carry no nullability attributes, so nullable warnings run on the modern targets only. A null-forgiving
// operator the modern targets need is inert on these two, and SonarSource S8970 reports every one of them there.
// Nothing is lost by accepting that: on the modern targets S8969 reports any null-forgiving operator that is not needed.
#if NETSTANDARD2_0 || NETFRAMEWORK
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "S8970", Scope = "module",
    Justification = "nullable warnings are off on this target by design; S8969 checks the same operators on the modern targets")]
#endif
