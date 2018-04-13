using System;
using System.Net;
using System.Runtime.InteropServices;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using NUnit.Framework;
using OpenRasta.Codecs;
using OpenRasta.Configuration;
using OpenRasta.DI;
using OpenRasta.DI.Windsor;
using OpenRasta.Hosting.HttpListener;
using OpenRasta.Web;

namespace WindsorWithHttpListenerHost_Specification
{
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
  public class when_creating_a_new_HttpListenerHost_with_WindsorResolver : IDisposable
  {
    static Random randomPort = new Random();
    HttpListenerHost _host;
    string Prefix;
    private IWindsorContainer _container;

    public when_creating_a_new_HttpListenerHost_with_WindsorResolver()
    {
      // init container
      _container = new WindsorContainer();
      var port = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? 18981 : randomPort.Next(1024, 2048);
      
      Prefix = $"http://localhost:{port}/";

      _container.Register(
        Component.For<IConfigurationSource>().ImplementedBy<TestConfigurationSource>().LifestyleSingleton());


      _host = new HttpListenerHost(new TestConfigurationSource(), new WindsorDependencyResolver(_container));
      _host.Initialize(new[] {Prefix}, "/");
      _host.StartListening();
    }


    public void Dispose()
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