﻿using System;
using System.Net;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using NUnit.Framework;
using OpenRasta.Codecs;
using OpenRasta.Configuration;
using OpenRasta.DI;
using OpenRasta.DI.Windsor;
using OpenRasta.Hosting.HttpListener;
using OpenRasta.Tests.Unit.Infrastructure;
using OpenRasta.Web;

namespace WindsorWithHttpListenerHost_Specification
{
    public class TestHttpListenerHostWithConfig : HttpListenerHost
    {
        private readonly IConfigurationSource _source;
        public IDependencyResolver Resolver { get; private set; }

        public TestHttpListenerHostWithConfig(IConfigurationSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public override bool ConfigureRootDependencies(IDependencyResolver resolver)
        {
            var configured = base.ConfigureRootDependencies(resolver);
            if (configured && _source != null)
            {
                resolver.AddDependencyInstance<IConfigurationSource>(_source);
            }
            Resolver = resolver;
            return configured;
        }
    }

    class TestConfigurationSource : IConfigurationSource
    {
        public void Configure()
        {
                ResourceSpace.Has.ResourcesOfType<string>()
                    .AtUri("/").Named("Root")
                    .And.AtUri("/with-header").Named("WithHeader")
                    .HandledBy<TestHandler>()
                    .And.HandledBy<HeaderSettingHandler>()
                    .TranscodedBy<TextPlainCodec>();
        }
    }

    public class TestHandler
    {
        [HttpOperation(ForUriName = "Root")]
        public string Get()
        {
            return "Test Root Response";
        }
    }

    public class HeaderSettingHandler
    {
        readonly IResponse _response;

        public HeaderSettingHandler(IResponse response)
        {
            _response = response;
        }

        [HttpOperation(ForUriName = "WithHeader")]
        public string Get()
        {
            _response.Headers["FOO"] = "BAR";
            return "Test Header Response";
        }
    }

    public class WindsorDependencyResolverAccessor : IDependencyResolverAccessor
    {
        private static IWindsorContainer _container;

        public IDependencyResolver Resolver { get; private set; }

        public WindsorDependencyResolverAccessor()
        {
            Resolver = new WindsorDependencyResolver(_container);
        }

        public static void SetupWith(IWindsorContainer container)
        {
            if (container == null)
                throw new ArgumentNullException("container");

            if (_container == null)
                _container = container;
        }
    }

    public class when_creating_a_new_HttpListenerHost_with_WindsorResolver : context
    {
        static Random randomPort = new Random();
        TestHttpListenerHostWithConfig _host;
        string Prefix;
        private IWindsorContainer _container;

        protected override void SetUp()
        {
            // init container
            _container = new WindsorContainer();
            Prefix = $"http://localhost:{randomPort.Next(1024, 2048)}/";

            _container.Register(
                Component.For<IConfigurationSource>().ImplementedBy<TestConfigurationSource>().LifestyleSingleton(),
                Component.For<TestHttpListenerHostWithConfig>().ImplementedBy<TestHttpListenerHostWithConfig>().LifestyleSingleton(),
                
                Component.For<IRequest>().UsingFactoryMethod(() => (IRequest)null).LifestyleScoped(),
                Component.For<IResponse>().UsingFactoryMethod(() => (IResponse)null).LifestyleScoped(),
            Component.For<ICommunicationContext>().UsingFactoryMethod(() => (ICommunicationContext)null).LifestyleScoped());

            // Statically set the container for the resolver accessor type as it is created by 
            // Activator.CreateInstance(Type type) in HttpListenerHost.Initialize(...)
            WindsorDependencyResolverAccessor.SetupWith(_container);

            var resolverFactoryType = typeof(WindsorDependencyResolverAccessor);

            _host = _container.Resolve<TestHttpListenerHostWithConfig>();
            _host.Initialize(new[] { Prefix }, "/", resolverFactoryType);
            _host.StartListening();
        }

        protected override void TearDown()
        {
            _host.StopListening();
            _host.Close();
        }

        [Test]
        public void the_resolver_is_a_windsor_dependency_resolver()
        {
            Assert.That(_host.Resolver, Is.Not.Null);
            Assert.That(_host.Resolver, Is.InstanceOf<WindsorDependencyResolver>());
        }

        [Test]
        public void the_root_uri_serves_the_test_string()
        {
            var response = new WebClient().DownloadString(Prefix);
            Assert.That(response, Is.EqualTo("Test Root Response"));
        }

        [Test]
        public void the_with_header_uri_serves_the_response_header()
        {
            var webClient = new WebClient();
            var response = webClient.DownloadString(Prefix + "with-header");
            Assert.That(response, Is.EqualTo("Test Header Response"));

            var fooHeader = webClient.ResponseHeaders["FOO"];
            Assert.That(fooHeader, Is.EqualTo("BAR"));
        }
    } 
}