#region License

/* Authors:
 *      Sebastien Lambla (seb@serialseb.com)
 * Copyright:
 *      (C) 2007-2009 Caffeine IT & naughtyProd Ltd (http://www.caffeine-it.com)
 * License:
 *      This file is distributed under the terms of the MIT License found at the end of this file.
 */

#endregion

using Castle.Core;
using Castle.Core.Internal;
using Castle.Facilities.TypedFactory;
using Castle.MicroKernel;
using Castle.MicroKernel.Registration;
using Castle.MicroKernel.Resolvers.SpecializedResolvers;
using Castle.Windsor;
using OpenRasta.Configuration.MetaModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Castle.MicroKernel.Lifestyle;
using OpenRasta.Web;

namespace OpenRasta.DI.Windsor
{
  public class WindsorDependencyResolver :
    DependencyResolverCore,
    IDependencyResolver,
    IModelDrivenDependencyRegistration,
    IRequestScopedResolver,
    IDisposable
  {
    readonly IWindsorContainer _windsorContainer;
    readonly bool _disposeContainerOnCleanup;
    static readonly object ContainerLock = new object();

    public WindsorDependencyResolver() : this(new WindsorContainer(), true)
    {
    }

    public WindsorDependencyResolver(IWindsorContainer container, bool disposeContainerOnCleanup = false)
    {
      _windsorContainer = container;
      _disposeContainerOnCleanup = disposeContainerOnCleanup;

      _windsorContainer.Kernel.Resolver.AddSubResolver(new CollectionResolver(container.Kernel, true));

      if (_windsorContainer.Kernel.GetFacilities().All(x => x.GetType() != typeof(TypedFactoryFacility)))
      {
        _windsorContainer.AddFacility<TypedFactoryFacility>();
      }

      _windsorContainer.Register(
        Component
          .For<IDependencyResolver, IModelDrivenDependencyRegistration>()
          .Instance(this)
          .OnlyNewServices());
//        Component.For<ICommunicationContext>().UsingFactoryMethod(() => (ICommunicationContext) null).LifestyleScoped()
      );
    }

    public bool HasDependency(Type serviceType)
    {
      if (serviceType == null) return false;
      return _windsorContainer.Kernel.GetHandlers(serviceType).Any();
    }

    public bool HasDependencyImplementation(Type serviceType, Type concreteType)
    {
      return
        _windsorContainer.Kernel.GetHandlers(serviceType)
          .Any(h => h.ComponentModel.Implementation == concreteType);
    }

    public void HandleIncomingRequestProcessed()
    {
      throw new NotSupportedException("Unsupported, are you sure you're using OpenRasta 2.6?");
    }

    static readonly ConcurrentDictionary<Type, Type> EnumMappings = new ConcurrentDictionary<Type, Type>();

    protected override object ResolveCore(Type serviceType)
    {
      var enumType = EnumMappings.GetOrAdd(serviceType, type => serviceType.GetCompatibleArrayItemType());

      return enumType != null
        ? _windsorContainer.ResolveAll(enumType)
        : _windsorContainer.Resolve(serviceType);
    }

    protected override IEnumerable<TService> ResolveAllCore<TService>()
    {
      return _windsorContainer.ResolveAll<TService>();
    }

    protected override void AddDependencyCore(Type dependent, Type concrete, DependencyLifetime lifetime)
    {
      string componentName = Guid.NewGuid().ToString();
      lock (ContainerLock)
      {
        _windsorContainer.Register(Component.For(dependent).ImplementedBy(concrete).Named(componentName).LifeStyle
          .Is(ConvertLifestyles.ToLifestyleType(lifetime)));
      }
    }

    protected override void AddDependencyInstanceCore(Type serviceType, object instance, DependencyLifetime lifetime)
    {
      string key = Guid.NewGuid().ToString();
      lock (ContainerLock)
      {
        _windsorContainer.Register(Component
          .For(serviceType)
          .Instance(instance)
          .Named(key)
          .IsDefault()
          .LifeStyle.Is(ConvertLifestyles.ToLifestyleType(lifetime)));
      }
    }

    protected override void AddDependencyCore(Type handlerType, DependencyLifetime lifetime)
    {
      AddDependencyCore(handlerType, handlerType, lifetime);
    }

    public void Dispose()
    {
      if (_disposeContainerOnCleanup)
      {
        _windsorContainer?.Dispose();
      }
    }

    public void Register(DependencyFactoryModel registration)
    {
      var genericTypeDef = registration.GetType().GetGenericTypeDefinition();
      var args = registration.GetType().GenericTypeArguments;
      var allArgs = new[] {registration.ServiceType}.Concat(args).ToArray();

      Type registrarType = null;
      if (genericTypeDef == typeof(DependencyFactoryModel<>))
        registrarType = typeof(FactoryRegistration<,>).MakeGenericType(allArgs);
      else if (genericTypeDef == typeof(DependencyFactoryModel<,>))
        registrarType = typeof(FactoryRegistration<,,>).MakeGenericType(allArgs);
      else if (genericTypeDef == typeof(DependencyFactoryModel<,,>))
        registrarType = typeof(FactoryRegistration<,,,>).MakeGenericType(allArgs);

      else if (genericTypeDef == typeof(DependencyFactoryModel<,,,>))
        registrarType = typeof(FactoryRegistration<,,,,>).MakeGenericType(allArgs);

      else if (genericTypeDef == typeof(DependencyFactoryModel<,,,,>))
        registrarType = typeof(FactoryRegistration<,,,,,>).MakeGenericType(allArgs);
      else
        throw new NotSupportedException();

      var registrar = (IRegisterFactories) Activator.CreateInstance(registrarType);

      registrar.Register(_windsorContainer, registration);
    }

    public IDisposable CreateRequestScope()
    {
      return _windsorContainer.BeginScope();
    }

    interface IRegisterFactories
    {
      void Register(IWindsorContainer container, DependencyFactoryModel model);
    }

    class FactoryRegistration<TService, TConcrete> : IRegisterFactories
      where TConcrete : TService where TService : class
    {
      public void Register(IWindsorContainer container, DependencyFactoryModel registration)
      {
        var name = Guid.NewGuid().ToString();
        var factoryMethod = ((Expression<Func<TConcrete>>) registration.Factory).Compile();
        container.Register(
          Component.For<TService>()
            .Named(name)
            .UsingFactoryMethod(factoryMethod)
            .LifeStyle.Is(ConvertLifestyles.ToLifestyleType(registration.Lifetime)));
      }
    }

    class FactoryRegistration<TService, TArg, TConcrete> : IRegisterFactories
      where TService : class where TConcrete : TService
    {
      public void Register(IWindsorContainer container, DependencyFactoryModel registration)
      {
        var name = Guid.NewGuid().ToString();
        var factoryMethod = ((Expression<Func<TArg, TConcrete>>) registration.Factory).Compile();
        container.Register(
          Component.For<TService>()
            .Named(name)
            .UsingFactoryMethod(kernel => factoryMethod(kernel.Resolve<TArg>()))
            .LifeStyle.Is(ConvertLifestyles.ToLifestyleType(registration.Lifetime)));
      }
    }

    class FactoryRegistration<TService, TArg1, TArg2, TConcrete> : IRegisterFactories
      where TService : class where TConcrete : TService
    {
      public void Register(IWindsorContainer container, DependencyFactoryModel registration)
      {
        var name = Guid.NewGuid().ToString();
        var factoryMethod = ((Expression<Func<TArg1, TArg2, TConcrete>>) registration.Factory).Compile();
        container.Register(
          Component.For<TService>()
            .Named(name)
            .UsingFactoryMethod(kernel => factoryMethod(kernel.Resolve<TArg1>(), kernel.Resolve<TArg2>()))
            .LifeStyle.Is(ConvertLifestyles.ToLifestyleType(registration.Lifetime)));
      }
    }

    class FactoryRegistration<TService, TArg1, TArg2, TArg3, TConcrete> : IRegisterFactories
      where TService : class where TConcrete : TService
    {
      public void Register(IWindsorContainer container, DependencyFactoryModel registration)
      {
        var name = Guid.NewGuid().ToString();
        var factoryMethod = ((Expression<Func<TArg1, TArg2, TArg3, TConcrete>>) registration.Factory).Compile();
        container.Register(
          Component.For<TService>()
            .Named(name)
            .UsingFactoryMethod(kernel => factoryMethod(
              kernel.Resolve<TArg1>(),
              kernel.Resolve<TArg2>(),
              kernel.Resolve<TArg3>()))
            .LifeStyle.Is(ConvertLifestyles.ToLifestyleType(registration.Lifetime)));
      }
    }

    class FactoryRegistration<TService, TArg1, TArg2, TArg3, TArg4, TConcrete> : IRegisterFactories
      where TService : class where TConcrete : TService
    {
      public void Register(IWindsorContainer container, DependencyFactoryModel registration)
      {
        var name = Guid.NewGuid().ToString();
        var factoryMethod = ((Expression<Func<TArg1, TArg2, TArg3, TArg4, TConcrete>>) registration.Factory).Compile();
        container.Register(
          Component.For<TService>()
            .Named(name)
            .UsingFactoryMethod(kernel => factoryMethod(
              kernel.Resolve<TArg1>(),
              kernel.Resolve<TArg2>(),
              kernel.Resolve<TArg3>(),
              kernel.Resolve<TArg4>()))
            .LifeStyle.Is(ConvertLifestyles.ToLifestyleType(registration.Lifetime)));
      }
    }
  }
}

#region Full license

// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

#endregion