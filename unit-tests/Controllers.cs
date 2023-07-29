using Microsoft.AspNetCore.Mvc;
using Xunit.Abstractions;

namespace BeyondImmersion.UnitTests.Controllers;

[Collection("unit tests")]
public class Controllers : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    public class TestController_NoAttribute : ControllerBase, IDaprController { }

    [DaprController]
    public class TestController_NoService : ControllerBase, IDaprController { }

    [DaprController(serviceType: typeof(TestService_SingleController))]
    public class TestController_SingleController : ControllerBase, IDaprController { }

    [DaprService("ControllerTests.OneControllerTest")]
    public class TestService_SingleController : IDaprService { }

    [DaprController(serviceType: typeof(TestService_MultipleControllers))]
    public class TestControllerA_MultipleControllers : ControllerBase, IDaprController { }

    [DaprController(serviceType: typeof(TestService_MultipleControllers))]
    public class TestControllerB_MultipleControllers : ControllerBase, IDaprController { }

    [DaprService("ControllerTests.MultipleControllerTest")]
    public class TestService_MultipleControllers : IDaprService { }

    private Controllers(CollectionFixture collectionContext) => TestCollectionContext = collectionContext;

    public Controllers(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Controllers>();
    }

    private void ResetENVs()
    {
        Environment.SetEnvironmentVariable("ControllerTests.OneControllerTest_Service_Enabled".ToUpper(), null);
        Environment.SetEnvironmentVariable("ControllerTests.MultipleControllerTest_Service_Enabled".ToUpper(), null);

        TestCollectionContext.ResetENVs();
    }

    [Fact]
    public void FindAllControllers()
    {
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindAll();
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoAttribute));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestController_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestController_SingleController));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestControllerB_MultipleControllers));
    }

    [Fact]
    public void FindAllControllers_EnabledOnly()
    {
        ResetENVs();

        Environment.SetEnvironmentVariable("ControllerTests.OneControllerTest_Service_Enabled".ToUpper(), "false");
        Environment.SetEnvironmentVariable("ControllerTests.MultipleControllerTest_Service_Enabled".ToUpper(), "false");
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindAll(enabledOnly: true);
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoAttribute));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestController_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestControllerB_MultipleControllers));

        Environment.SetEnvironmentVariable("ControllerTests.OneControllerTest_Service_Enabled".ToUpper(), "true");
        Environment.SetEnvironmentVariable("ControllerTests.MultipleControllerTest_Service_Enabled".ToUpper(), "false");
        allControllers = IDaprController.FindAll(enabledOnly: true);
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoAttribute));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestController_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestController_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestControllerB_MultipleControllers));

        Environment.SetEnvironmentVariable("ControllerTests.OneControllerTest_Service_Enabled".ToUpper(), "false");
        Environment.SetEnvironmentVariable("ControllerTests.MultipleControllerTest_Service_Enabled".ToUpper(), "true");
        allControllers = IDaprController.FindAll(enabledOnly: true);
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoAttribute));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestController_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_SingleController));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestControllerB_MultipleControllers));
    }

    [Fact]
    public void FindAllControllers_ByService_OneController()
    {
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForHandler(typeof(TestService_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestController_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestControllerB_MultipleControllers));
    }

    [Fact]
    public void FindAllControllers_ByService_MultipleControllers()
    {
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForHandler(typeof(TestService_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_SingleController));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestControllerB_MultipleControllers));
    }

    [Fact]
    public void FindAllControllers_ByService_OneController_Generic()
    {
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForHandler<TestService_SingleController>();
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoService));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestController_SingleController));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestControllerA_MultipleControllers));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestControllerB_MultipleControllers));
    }

    [Fact]
    public void FindAllControllers_ByService_MultipleControllers_Generic()
    {
        (Type, DaprControllerAttribute)[] allControllers = IDaprController.FindForHandler<TestService_MultipleControllers>();
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoAttribute));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_NoService));
        Assert.DoesNotContain(allControllers, t => t.Item1 == typeof(TestController_SingleController));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestControllerA_MultipleControllers));
        Assert.Contains(allControllers, t => t.Item1 == typeof(TestControllerB_MultipleControllers));
    }
}
