# Dotnet8DiBugCase

https://github.com/dotnet/runtime/issues/88968

Steps:

1. Build using NET7
1. Run using NET7 - ok
1. Build using NET8
1. Run using NET8

ER: ok
AR: System.InvalidOperationException: Unable to resolve service for type 'Castle.DynamicProxy.IInterceptor[]' while attempting to activate 'Castle.Proxies.ITestServiceProxy'.
