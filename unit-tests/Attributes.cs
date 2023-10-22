using System.Reflection;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class Attributes : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    private class TestClassAttributeA : Attribute, IServiceAttribute { }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    private class TestClassAttributeB : BaseServiceAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    private class TestMethodAttributeA : Attribute, IServiceAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    private class TestMethodAttributeB : BaseServiceAttribute { }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    private class TestFieldAttributeA : Attribute, IServiceAttribute { }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    private class TestFieldAttributeB : BaseServiceAttribute { }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    private class TestPropertyAttributeA : Attribute, IServiceAttribute { }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    private class TestPropertyAttributeB : BaseServiceAttribute { }

    [TestClassAttributeA]
    private class TestClassA
    {
        public TestClassA() { }
    }

    [TestClassAttributeB]
    private class TestClassB
    {
        public TestClassB() { }
    }

    [TestClassAttributeA]
    private class TestMethodClassA
    {
        public TestMethodClassA() { }

        [TestMethodAttributeA]
        public void TestMethodA() { }
        public void TestMethodB() { }
        public void TestMethodC() { }
    }

    [TestClassAttributeB]
    private class TestMethodClassB
    {
        public TestMethodClassB() { }

        private int TestMethodA() => 1;
        [TestMethodAttributeB]
        private int TestMethodB() => 1;
        private int TestMethodC() => 1;
    }

    private class TestMethodClassA_NoAttr
    {
        public TestMethodClassA_NoAttr() { }

        [TestMethodAttributeA]
        public void TestMethodA() { }
        public void TestMethodB() { }
        public void TestMethodC() { }
    }

    [TestClassAttributeA]
    private class TestPropertyClassA
    {
        [TestPropertyAttributeA]
        public string PropA { get; }
        public string PropB { get; }
        public string PropC { get; }

        public TestPropertyClassA(string propA, string propB, string propC)
        {
            PropA = propA;
            PropB = propB;
            PropC = propC;
        }
    }

    [TestClassAttributeB]
    private class TestPropertyClassB
    {
        private string PropA { get; set; }
        [TestPropertyAttributeB]
        private string PropB { get; set; }
        private string PropC { get; set; }

        public TestPropertyClassB(string propA, string propB, string propC)
        {
            PropA = propA;
            PropB = propB;
            PropC = propC;
        }
    }

    private class TestPropertyClassA_NoAttr
    {
        [TestPropertyAttributeA]
        public string PropA { get; }
        public string PropB { get; }
        public string PropC { get; }

        public TestPropertyClassA_NoAttr(string propA, string propB, string propC)
        {
            PropA = propA;
            PropB = propB;
            PropC = propC;
        }
    }

    [TestClassAttributeA]
    private class TestFieldClassA
    {
        [TestFieldAttributeA]
        public string FieldA;
        public string FieldB;
        public string FieldC;

        public TestFieldClassA(string fieldA, string fieldB, string fieldC)
        {
            FieldA = fieldA;
            FieldB = fieldB;
            FieldC = fieldC;
        }
    }

    [TestClassAttributeB]
    private class TestFieldClassB
    {
        private readonly string FieldA = "Default";
        [TestFieldAttributeB]
        private readonly string FieldB = "Default";
        private readonly string FieldC = "Default";

        public TestFieldClassB(string fieldA, string fieldB, string fieldC)
        {
            FieldA = fieldA;
            FieldB = fieldB;
            FieldC = fieldC;
        }
    }

    private class TestFieldClassA_NoAttr
    {
        [TestFieldAttributeA]
        private readonly string FieldA;
        private readonly string FieldB;
        private readonly string FieldC;

        public TestFieldClassA_NoAttr(string name, string value, string type)
        {
            FieldA = name;
            FieldB = value;
            FieldC = type;
        }
    }

    private Attributes(CollectionFixture collectionContext) => TestCollectionContext = collectionContext;

    public Attributes(CollectionFixture collectionContext, ITestOutputHelper output)
    {
        TestCollectionContext = collectionContext;
        Program.Logger = output.BuildLoggerFor<Attributes>();
    }

    private BindingFlags UseAllBindingFlags()
        => BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

    [Fact]
    public void GetClassWithAttribute()
    {
        List<(Type, IServiceAttribute)> withAttr = IServiceAttribute.GetClassesWithAttribute(typeof(TestClassAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 == typeof(TestClassA));
        Assert.DoesNotContain(withAttr, t => t.Item1 == typeof(TestClassB));

        withAttr = IServiceAttribute.GetClassesWithAttribute(typeof(TestClassAttributeB));
        Assert.NotNull(withAttr);
        Assert.DoesNotContain(withAttr, t => t.Item1 == typeof(TestClassA));
        Assert.Contains(withAttr, t => t.Item1 == typeof(TestClassB));
    }

    [Fact]
    public void GetClassWithAttribute_Generic()
    {
        List<(Type, TestClassAttributeA)> withAttrA = IServiceAttribute.GetClassesWithAttribute<TestClassAttributeA>();
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item1 == typeof(TestClassA));
        Assert.DoesNotContain(withAttrA, t => t.Item1 == typeof(TestClassB));

        List<(Type, TestClassAttributeB)> withAttrB = IServiceAttribute.GetClassesWithAttribute<TestClassAttributeB>();
        Assert.NotNull(withAttrB);
        Assert.DoesNotContain(withAttrB, t => t.Item1 == typeof(TestClassA));
        Assert.Contains(withAttrB, t => t.Item1 == typeof(TestClassB));
    }

    [Fact]
    public void GetMethodWithAttribute()
    {
        List<(Type, MethodInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetMethodsWithAttribute(typeof(TestMethodAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodC), UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetMethodsWithAttribute(typeof(TestMethodAttributeB));
        Assert.NotNull(withAttr);
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestMethodClassB).GetMethod("TestMethodA", UseAllBindingFlags()));
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(TestMethodClassB).GetMethod("TestMethodB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestMethodClassB).GetMethod("TestMethodC", UseAllBindingFlags()));
    }

    [Fact]
    public void GetMethodWithAttribute_Generic()
    {
        List<(Type, MethodInfo, TestMethodAttributeA)> withAttrA = IServiceAttribute.GetMethodsWithAttribute<TestMethodAttributeA>();
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item2 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodC), UseAllBindingFlags()));

        List<(Type, MethodInfo, TestMethodAttributeB)> withAttrB = IServiceAttribute.GetMethodsWithAttribute<TestMethodAttributeB>();
        Assert.NotNull(withAttrB);
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(TestMethodClassB).GetMethod("TestMethodA", UseAllBindingFlags()));
        Assert.Contains(withAttrB, t => t.Item2 ==
            typeof(TestMethodClassB).GetMethod("TestMethodB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(TestMethodClassB).GetMethod("TestMethodC", UseAllBindingFlags()));
    }

    [Fact]
    public void GetPropertyWithAttribute()
    {
        List<(Type, PropertyInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetPropertiesWithAttribute(typeof(TestPropertyAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropC), UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetPropertiesWithAttribute(typeof(TestPropertyAttributeB));
        Assert.NotNull(withAttr);
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestPropertyClassB).GetProperty("PropA", UseAllBindingFlags()));
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(TestPropertyClassB).GetProperty("PropB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestPropertyClassB).GetProperty("PropC", UseAllBindingFlags()));
    }

    [Fact]
    public void GetPropertyWithAttribute_Generic()
    {
        List<(Type, PropertyInfo, TestPropertyAttributeA)> withAttrA = IServiceAttribute.GetPropertiesWithAttribute<TestPropertyAttributeA>();
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item2 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropC), UseAllBindingFlags()));

        List<(Type, PropertyInfo, TestPropertyAttributeB)> withAttrB = IServiceAttribute.GetPropertiesWithAttribute<TestPropertyAttributeB>();
        Assert.NotNull(withAttrB);
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(TestPropertyClassB).GetProperty("PropA", UseAllBindingFlags()));
        Assert.Contains(withAttrB, t => t.Item2 ==
            typeof(TestPropertyClassB).GetProperty("PropB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(TestPropertyClassB).GetProperty("PropC", UseAllBindingFlags()));
    }

    [Fact]
    public void GetFieldWithAttribute()
    {
        List<(Type, FieldInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetFieldsWithAttribute(typeof(TestFieldAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldC), UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetFieldsWithAttribute(typeof(TestFieldAttributeB));
        Assert.NotNull(withAttr);
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestFieldClassB).GetField("FieldA", UseAllBindingFlags()));
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(TestFieldClassB).GetField("FieldB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(TestFieldClassB).GetField("FieldC", UseAllBindingFlags()));
    }

    [Fact]
    public void GetFieldWithAttribute_Generic()
    {
        List<(Type, FieldInfo, TestFieldAttributeA)> withAttrA = IServiceAttribute.GetFieldsWithAttribute<TestFieldAttributeA>();
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item2 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldC), UseAllBindingFlags()));

        List<(Type, FieldInfo, TestFieldAttributeB)> withAttrB = IServiceAttribute.GetFieldsWithAttribute<TestFieldAttributeB>();
        Assert.NotNull(withAttrB);
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(TestFieldClassB).GetField("FieldA", UseAllBindingFlags()));
        Assert.Contains(withAttrB, t => t.Item2 ==
            typeof(TestFieldClassB).GetField("FieldB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(TestFieldClassB).GetField("FieldC", UseAllBindingFlags()));
    }

    [Fact]
    public void GetMethodWithAttribute_SpecificClass()
    {
        List<(MethodInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetMethodsWithAttribute(typeof(TestMethodClassA), typeof(TestMethodAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(TestMethodClassB).GetMethod("TestMethodA", UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetMethodsWithAttribute(typeof(TestMethodClassB), typeof(TestMethodAttributeB));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(TestMethodClassB).GetMethod("TestMethodB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodB), UseAllBindingFlags()));
    }

    [Fact]
    public void GetMethodWithAttribute_SpecificClass_Generic()
    {
        List<(MethodInfo, TestMethodAttributeA)> withAttrA = IServiceAttribute.GetMethodsWithAttribute<TestMethodAttributeA>(typeof(TestMethodClassA));
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item1 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item1 ==
            typeof(TestMethodClassB).GetMethod("TestMethodA", UseAllBindingFlags()));

        List<(MethodInfo, TestMethodAttributeB)> withAttrB = IServiceAttribute.GetMethodsWithAttribute<TestMethodAttributeB>(typeof(TestMethodClassB));
        Assert.NotNull(withAttrB);
        Assert.Contains(withAttrB, t => t.Item1 ==
            typeof(TestMethodClassB).GetMethod("TestMethodB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item1 ==
            typeof(TestMethodClassA).GetMethod(nameof(TestMethodClassA.TestMethodB), UseAllBindingFlags()));
    }

    [Fact]
    public void GetPropertyWithAttribute_SpecificClass()
    {
        List<(PropertyInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetPropertiesWithAttribute(typeof(TestPropertyClassA), typeof(TestPropertyAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(TestPropertyClassB).GetProperty("PropA", UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetPropertiesWithAttribute(typeof(TestPropertyClassB), typeof(TestPropertyAttributeB));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(TestPropertyClassB).GetProperty("PropB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropB), UseAllBindingFlags()));
    }

    [Fact]
    public void GetPropertyWithAttribute_SpecificClass_Generic()
    {
        List<(PropertyInfo, TestPropertyAttributeA)> withAttrA = IServiceAttribute.GetPropertiesWithAttribute<TestPropertyAttributeA>(typeof(TestPropertyClassA));
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item1 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item1 ==
            typeof(TestPropertyClassB).GetProperty("PropA", UseAllBindingFlags()));

        List<(PropertyInfo, TestPropertyAttributeB)> withAttrB = IServiceAttribute.GetPropertiesWithAttribute<TestPropertyAttributeB>(typeof(TestPropertyClassB));
        Assert.NotNull(withAttrB);
        Assert.Contains(withAttrB, t => t.Item1 ==
            typeof(TestPropertyClassB).GetProperty("PropB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item1 ==
            typeof(TestPropertyClassA).GetProperty(nameof(TestPropertyClassA.PropB), UseAllBindingFlags()));
    }

    [Fact]
    public void GetFieldWithAttribute_SpecificClass()
    {
        List<(FieldInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetFieldsWithAttribute(typeof(TestFieldClassA), typeof(TestFieldAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(TestFieldClassB).GetField("FieldA", UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetFieldsWithAttribute(typeof(TestFieldClassB), typeof(TestFieldAttributeB));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(TestFieldClassB).GetField("FieldB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldB), UseAllBindingFlags()));
    }

    [Fact]
    public void GetFieldWithAttribute_SpecificClass_Generic()
    {
        List<(FieldInfo, TestFieldAttributeA)> withAttrA = IServiceAttribute.GetFieldsWithAttribute<TestFieldAttributeA>(typeof(TestFieldClassA));
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item1 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item1 ==
            typeof(TestFieldClassB).GetField("FieldA", UseAllBindingFlags()));

        List<(FieldInfo, TestFieldAttributeB)> withAttrB = IServiceAttribute.GetFieldsWithAttribute<TestFieldAttributeB>(typeof(TestFieldClassB));
        Assert.NotNull(withAttrB);
        Assert.Contains(withAttrB, t => t.Item1 ==
            typeof(TestFieldClassB).GetField("FieldB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item1 ==
            typeof(TestFieldClassA).GetField(nameof(TestFieldClassA.FieldB), UseAllBindingFlags()));
    }
}
