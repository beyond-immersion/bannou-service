using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class Controllers : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public class Controller_NoAttribute : ControllerBase, IDaprController { }

    [DaprController]
    public class Controller_NoService : ControllerBase, IDaprController { }

    [DaprController(interfaceType: typeof(Service_SingleController))]
    public class Controller_SingleController : ControllerBase, IDaprController { }

    [DaprService("ControllerTests.OneController", interfaceType: typeof(Service_SingleController))]
    public class Service_SingleController : IDaprService { }

    [DaprController(interfaceType: typeof(Service_MultipleControllers))]
    public class ControllerA_MultipleControllers : ControllerBase, IDaprController { }

    [DaprController(interfaceType: typeof(Service_MultipleControllers))]
    public class ControllerB_MultipleControllers : ControllerBase, IDaprController { }

    [DaprService("ControllerTests.MultipleControllers", interfaceType: typeof(Service_MultipleControllers))]
    public class Service_MultipleControllers : IDaprService { }

    public interface ITestService_FromInterface : IDaprService { }
    [DaprService("ControllerTests.Interface", typeof(ITestService_FromInterface))]
    public class Service_FromInterface: ITestService_FromInterface { }

    [DaprController(typeof(ITestService_FromInterface))]
    public class Controller_FromInterface: ControllerBase, IDaprController { }

    public Controllers(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Controllers>();
    }

    [Fact]
    public void Controllers_FindAllNonServiceControllers()
    {
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.NonServiceControllers;
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindAllControllers()
    {
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.Controllers;
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }

    [Fact]
    public void Controllers_FindAllServiceControllers()
    {
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.ServiceControllers;
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
            var serviceControllers = IDaprController.EnabledServiceControllers;
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoAttribute));
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoService));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(Controller_SingleController));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));

            Environment.SetEnvironmentVariable("ControllerTests.OneController_Service_Disabled".ToUpper(), "true");
            Environment.SetEnvironmentVariable("ControllerTests.MultipleControllers_Service_Disabled".ToUpper(), "false");
            serviceControllers = IDaprController.EnabledServiceControllers;
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoAttribute));
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_NoService));
            Assert.DoesNotContain(serviceControllers, t => t.Item1 == typeof(Controller_SingleController));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
            Assert.Contains(serviceControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));

            Environment.SetEnvironmentVariable("ControllerTests.OneController_Service_Disabled".ToUpper(), "false");
            Environment.SetEnvironmentVariable("ControllerTests.MultipleControllers_Service_Disabled".ToUpper(), "true");
            serviceControllers = IDaprController.EnabledServiceControllers;
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForInterface(typeof(ITestService_FromInterface));
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForInterface(typeof(Service_SingleController));
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForInterface(typeof(Service_MultipleControllers));
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForInterface<Service_SingleController>();
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForInterface<Service_MultipleControllers>();
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForImplementation(typeof(Service_FromInterface));
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForImplementation(typeof(Service_SingleController));
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForImplementation(typeof(Service_MultipleControllers));
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForImplementation<Service_SingleController>();
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
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForImplementation<Service_MultipleControllers>();
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(Controller_FromInterface));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(ControllerB_MultipleControllers));
    }
}
