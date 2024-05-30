using Castle.DynamicProxy;
using Castle.DynamicProxy.Generators;
using Castle.DynamicProxy.Generators.Emitters;
using Castle.DynamicProxy.Generators.Emitters.SimpleAST;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Test
{
    public interface ITestService
    {
        string DoTest();
    }

    public class TestService : ITestService
    {
        public string DoTest() => "TestService";
    }

    public class TestInterceptor : IInterceptor
    {
        public static bool Invoked { get; set; }

        public void Intercept(IInvocation invocation)
        {
            Invoked = true;
            invocation.Proceed();
        }
    }

    public class RuntimeInterceptorSelectorAccessor<TImplementation>
    {
        public RuntimeInterceptorSelectorAccessor(Type[] interceptorTypes)
        {
            InterceptorTypes = interceptorTypes;
        }

        public Type[] InterceptorTypes { get; }
    }

    public class RuntimeInterceptorSelector<TImplementation> : IInterceptorSelector
    {
        private readonly IInterceptor[] _interceptors;

        public RuntimeInterceptorSelector(RuntimeInterceptorSelectorAccessor<TImplementation> accessor, IServiceProvider serviceProvider)
        {
            _interceptors = accessor.InterceptorTypes.Select(t => (IInterceptor)serviceProvider.GetRequiredService(t)).ToArray();
        }

        public IInterceptor[] SelectInterceptors(Type type, MethodInfo method, IInterceptor[] interceptors) => _interceptors;
    }

    public class TypedInterfaceProxyWithTargetGenerator : InterfaceProxyWithTargetGenerator
    {
        private readonly Type _implementationType;

        private static readonly ProxyGenerator ProxyGenerator = new();

        public TypedInterfaceProxyWithTargetGenerator(Type interfaceType, Type implementationType)
            : base(ProxyGenerator.ProxyBuilder.ModuleScope,
                interfaceType)
        {
            _implementationType = implementationType;
            Logger = ProxyGenerator.ProxyBuilder.Logger;
        }

        protected override void CreateFields(ClassEmitter emitter)
        {
            CreateOptionsField(emitter);
            CreateInterceptorsField(emitter);
        }

        protected override Type Init(
            string typeName,
            out ClassEmitter emitter,
            Type proxyTargetType,
            out FieldReference interceptorsField,
            IEnumerable<Type> interfaces)
        {
            var forInterfaceProxy = ProxyGenerationOptions.BaseTypeForInterfaceProxy;

            emitter = BuildClassEmitter(typeName, forInterfaceProxy, interfaces);

            CreateFields(emitter);

            // Обязательно добавляем типизированное поле для реализации
            targetField = emitter.CreateField("__target", _implementationType);

            // Обязательно добавляем поле и параметр конструктора для селектора
            emitter.CreateField("__selector", typeof(RuntimeInterceptorSelector<>).MakeGenericType(_implementationType));

            CreateTypeAttributes(emitter);

            interceptorsField = emitter.GetField("__interceptors");

            return forInterfaceProxy;
        }
    }

    internal static class Extensions
    {
        public static IServiceCollection AddScopedWithInterceptors<TService, TImplementation, TInterceptor>(
            this IServiceCollection serviceCollection)
            where TService : class
            where TImplementation : class, TService
            where TInterceptor : class, IInterceptor =>
            AddWithInterceptors(serviceCollection, typeof(TService), typeof(TImplementation), new[] { typeof(TInterceptor) }, ServiceLifetime.Scoped);

        private static IServiceCollection AddWithInterceptors(
            this IServiceCollection serviceCollection,
            Type serviceType,
            Type implementationType,
            Type[] interceptorTypes,
            ServiceLifetime serviceLifetime)
        {
            /*var withTargetGenerator = new TypedInterfaceProxyWithTargetGenerator(new ModuleScope(),
				serviceType,
				Type.EmptyTypes,
				implementationType,
				ProxyGenerationOptions.Default);
			var proxiedImplementationType = withTargetGenerator.GetProxyType();
            */
            var withTargetGenerator = new TypedInterfaceProxyWithTargetGenerator(serviceType, implementationType);
            var proxiedImplementationType = withTargetGenerator.GenerateCode(implementationType, Type.EmptyTypes, ProxyGenerationOptions.Default);

            serviceCollection.Add(new ServiceDescriptor(implementationType, implementationType, serviceLifetime));
            serviceCollection.Add(new ServiceDescriptor(serviceType, proxiedImplementationType, serviceLifetime));

            return serviceCollection
                .AddInterceptorsForType(interceptorTypes, implementationType);
        }

        internal static IServiceCollection AddInterceptorsForType(this IServiceCollection serviceCollection, Type[] interceptorTypes, Type implementationType)
        {
            foreach (var interceptor in interceptorTypes)
                serviceCollection.TryAddTransient(interceptor);

            var accessorType = typeof(RuntimeInterceptorSelectorAccessor<>).MakeGenericType(implementationType);

            return serviceCollection
                .AddTransient(typeof(RuntimeInterceptorSelector<>).MakeGenericType(implementationType))
                .AddSingleton(accessorType, Activator.CreateInstance(accessorType, (object)interceptorTypes));
        }
    }

    internal class Program
    {
        static void Main(string[] args)
        {
            var services = new ServiceCollection();

            services.AddScopedWithInterceptors<ITestService, TestService, TestInterceptor>();
            var b = services.BuildServiceProvider();
            var service = b.GetService<ITestService>();
            System.Console.WriteLine(service.DoTest());
        }
    }
}
