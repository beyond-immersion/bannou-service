namespace BeyondImmersion.ServiceTester.Tests
{
    public class TemplateTestHandler : IServiceTestHandler
    {
        public ServiceTest[] GetServiceTests()
        {
            return new ServiceTest[]
            {
                new ServiceTest(TemplateTest_CreateNew, "Templates - Create New", "HTTP",
                    "Attempt to create a new test template definition.")
            };
        }

        public void TemplateTest_CreateNew(string[] args)
        {

        }
    }
}
