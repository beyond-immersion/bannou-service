using BeyondImmersion.BannouService.Controllers.Messages;
using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class Controllers : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public class Controller_NoAttribute : ControllerBase, IBannouController { }

    [BannouController]
    public class Controller_NoService : ControllerBase, IBannouController { }

    [BannouController(interfaceType: typeof(Service_SingleController))]
    public class Controller_SingleController : ControllerBase, IBannouController { }

    [BannouService("ControllerTests.OneController", interfaceType: typeof(Service_SingleController))]
    public class Service_SingleController : IBannouService { }

    [BannouController(interfaceType: typeof(Service_MultipleControllers))]
    public class ControllerA_MultipleControllers : ControllerBase, IBannouController { }

    [BannouController(interfaceType: typeof(Service_MultipleControllers))]
    public class ControllerB_MultipleControllers : ControllerBase, IBannouController { }

    [BannouService("ControllerTests.MultipleControllers", interfaceType: typeof(Service_MultipleControllers))]
    public class Service_MultipleControllers : IBannouService { }

    public interface ITestService_FromInterface : IBannouService { }
    [BannouService("ControllerTests.Interface", typeof(ITestService_FromInterface))]
    public class Service_FromInterface : ITestService_FromInterface { }

    [BannouController(typeof(ITestService_FromInterface))]
    public class Controller_FromInterface : ControllerBase, IBannouController { }

    public Controllers(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Controllers>();
    }

    [Fact]
    public void Controllers_FindAllNonServiceControllers()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.NonServiceControllers;
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindAllControllers()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.Controllers;
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindAllServiceControllers()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.ServiceControllers;
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_Enabled()
    {
        Environment.SetEnvironmentVariable("ControllerTests.OneController_Service_Disabled".ToUpper(), null);
        Environment.SetEnvironmentVariable("ControllerTests.MultipleControllers_Service_Disabled".ToUpper(), null);

        try
        {
            var serviceControllers = IBannouController.EnabledServiceControllers;
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoAttribute));
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoService));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(Controller_SingleController));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));

            Environment.SetEnvironmentVariable("ControllerTests.OneController_Service_Disabled".ToUpper(), "true");
            Environment.SetEnvironmentVariable("ControllerTests.MultipleControllers_Service_Disabled".ToUpper(), "false");
            serviceControllers = IBannouController.EnabledServiceControllers;
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoAttribute));
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoService));
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_SingleController));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));

            Environment.SetEnvironmentVariable("ControllerTests.OneController_Service_Disabled".ToUpper(), "false");
            Environment.SetEnvironmentVariable("ControllerTests.MultipleControllers_Service_Disabled".ToUpper(), "true");
            serviceControllers = IBannouController.EnabledServiceControllers;
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoAttribute));
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoService));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(Controller_SingleController));
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ControllerTests.OneController_Service_Disabled".ToUpper(), null);
            Environment.SetEnvironmentVariable("ControllerTests.MultipleControllers_Service_Disabled".ToUpper(), null);
        }
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByInterface()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForInterface(typeof(ITestService_FromInterface));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByInterface_OneController()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForInterface(typeof(Service_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByInterface_MultipleControllers()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForInterface(typeof(Service_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByInterface_OneController_Generic()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForInterface<Service_SingleController>();
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByInterface_MultipleControllers_Generic()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForInterface<Service_MultipleControllers>();
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByImplementation()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForImplementation(typeof(Service_FromInterface));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByImplementation_OneController()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForImplementation(typeof(Service_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByImplementation_MultipleControllers()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForImplementation(typeof(Service_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByImplementation_OneController_Generic()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForImplementation<Service_SingleController>();
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindServiceControllers_ByImplementation_MultipleControllers_Generic()
    {
        (Type, BannouControllerAttribute)[] allControllers = IBannouController.FindForImplementation<Service_MultipleControllers>();
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }
}
