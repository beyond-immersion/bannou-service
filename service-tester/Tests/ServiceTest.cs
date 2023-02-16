using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeyondImmersion.ServiceTester.Tests
{
    public sealed class ServiceTest
    {
        public string Name { get; }
        public string Description { get; }
        public string Type { get; }
        public Action<string[]> Target { get; }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private ServiceTest() { }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public ServiceTest(Action<string[]> target, string name, string type, string description)
        {
            Name = name;
            Description = description;
            Type = type;
            Target = target;
        }
    }
}
