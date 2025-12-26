using System.Reflection;
using Xunit.Abstractions;

namespace BeyondImmersion.BannouService.UnitTests;

[Collection("unit tests")]
public class Attributes : IClassFixture<CollectionFixture>
{
    private CollectionFixture TestCollectionContext { get; }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    private class ClassAttributeA : Attribute, IServiceAttribute { }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    private class ClassAttributeB : BaseServiceAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    private class MethodAttributeA : Attribute, IServiceAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    private class MethodAttributeB : BaseServiceAttribute { }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    private class FieldAttributeA : Attribute, IServiceAttribute { }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    private class FieldAttributeB : BaseServiceAttribute { }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    private class PropertyAttributeA : Attribute, IServiceAttribute { }

    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    private class PropertyAttributeB : BaseServiceAttribute { }

    [ClassAttributeA]
    private class ClassA
    {
        public ClassA() { }
    }

    [ClassAttributeB]
    private class ClassB
    {
        public ClassB() { }
    }

    [ClassAttributeA]
    private class MethodClassA
    {
        public MethodClassA() { }

        [MethodAttributeA]
        public void MethodA() { }
        public void MethodB() { }
        public void MethodC() { }
    }

    [ClassAttributeB]
    private class MethodClassB
    {
        public MethodClassB() { }

        private int MethodA() => 1;
        [MethodAttributeB]
        private int MethodB() => 1;
        private int MethodC() => 1;
    }

    private class MethodClassA_NoAttr
    {
        public MethodClassA_NoAttr() { }

        [MethodAttributeA]
        public void MethodA() { }
        public void MethodB() { }
        public void MethodC() { }
    }

    [ClassAttributeA]
    private class PropertyClassA
    {
        [PropertyAttributeA]
        public string PropA { get; }
        public string PropB { get; }
        public string PropC { get; }

        public PropertyClassA(string propA, string propB, string propC)
        {
            PropA = propA;
            PropB = propB;
            PropC = propC;
        }
    }

    [ClassAttributeB]
    private class PropertyClassB
    {
        private string PropA { get; set; }
        [PropertyAttributeB]
        private string PropB { get; set; }
        private string PropC { get; set; }

        public PropertyClassB(string propA, string propB, string propC)
        {
            PropA = propA;
            PropB = propB;
            PropC = propC;
        }
    }

    private class PropertyClassA_NoAttr
    {
        [PropertyAttributeA]
        public string PropA { get; }
        public string PropB { get; }
        public string PropC { get; }

        public PropertyClassA_NoAttr(string propA, string propB, string propC)
        {
            PropA = propA;
            PropB = propB;
            PropC = propC;
        }
    }

    [ClassAttributeA]
    private class FieldClassA
    {
        [FieldAttributeA]
        public string FieldA;
        public string FieldB;
        public string FieldC;

        public FieldClassA(string fieldA, string fieldB, string fieldC)
        {
            FieldA = fieldA;
            FieldB = fieldB;
            FieldC = fieldC;
        }
    }

    [ClassAttributeB]
    private class FieldClassB
    {
        private readonly string FieldA = "Default";
        [FieldAttributeB]
        private readonly string FieldB = "Default";
        private readonly string FieldC = "Default";

        public FieldClassB(string fieldA, string fieldB, string fieldC)
        {
            FieldA = fieldA;
            FieldB = fieldB;
            FieldC = fieldC;
        }
    }

    private class FieldClassA_NoAttr
    {
        [FieldAttributeA]
        private readonly string FieldA;
        private readonly string FieldB;
        private readonly string FieldC;

        public FieldClassA_NoAttr(string name, string value, string type)
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
    public void Attributes_GetClass()
    {
        List<(Type, IServiceAttribute)> withAttr = IServiceAttribute.GetClassesWithAttribute(typeof(ClassAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 == typeof(ClassA));
        Assert.DoesNotContain(withAttr, t => t.Item1 == typeof(ClassB));

        withAttr = IServiceAttribute.GetClassesWithAttribute(typeof(ClassAttributeB));
        Assert.NotNull(withAttr);
        Assert.DoesNotContain(withAttr, t => t.Item1 == typeof(ClassA));
        Assert.Contains(withAttr, t => t.Item1 == typeof(ClassB));
    }

    [Fact]
    public void Attributes_GetClass_Generic()
    {
        List<(Type, ClassAttributeA)> withAttrA = IServiceAttribute.GetClassesWithAttribute<ClassAttributeA>();
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item1 == typeof(ClassA));
        Assert.DoesNotContain(withAttrA, t => t.Item1 == typeof(ClassB));

        List<(Type, ClassAttributeB)> withAttrB = IServiceAttribute.GetClassesWithAttribute<ClassAttributeB>();
        Assert.NotNull(withAttrB);
        Assert.DoesNotContain(withAttrB, t => t.Item1 == typeof(ClassA));
        Assert.Contains(withAttrB, t => t.Item1 == typeof(ClassB));
    }

    [Fact]
    public void Attributes_GetMethod()
    {
        List<(Type, MethodInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetMethodsWithAttribute(typeof(MethodAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodC), UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetMethodsWithAttribute(typeof(MethodAttributeB));
        Assert.NotNull(withAttr);
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(MethodClassB).GetMethod("MethodA", UseAllBindingFlags()));
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(MethodClassB).GetMethod("MethodB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(MethodClassB).GetMethod("MethodC", UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetMethod_Generic()
    {
        List<(Type, MethodInfo, MethodAttributeA)> withAttrA = IServiceAttribute.GetMethodsWithAttribute<MethodAttributeA>();
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item2 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodC), UseAllBindingFlags()));

        List<(Type, MethodInfo, MethodAttributeB)> withAttrB = IServiceAttribute.GetMethodsWithAttribute<MethodAttributeB>();
        Assert.NotNull(withAttrB);
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(MethodClassB).GetMethod("MethodA", UseAllBindingFlags()));
        Assert.Contains(withAttrB, t => t.Item2 ==
            typeof(MethodClassB).GetMethod("MethodB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(MethodClassB).GetMethod("MethodC", UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetProperty()
    {
        List<(Type, PropertyInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetPropertiesWithAttribute(typeof(PropertyAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropC), UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetPropertiesWithAttribute(typeof(PropertyAttributeB));
        Assert.NotNull(withAttr);
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(PropertyClassB).GetProperty("PropA", UseAllBindingFlags()));
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(PropertyClassB).GetProperty("PropB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(PropertyClassB).GetProperty("PropC", UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetProperty_Generic()
    {
        List<(Type, PropertyInfo, PropertyAttributeA)> withAttrA = IServiceAttribute.GetPropertiesWithAttribute<PropertyAttributeA>();
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item2 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropC), UseAllBindingFlags()));

        List<(Type, PropertyInfo, PropertyAttributeB)> withAttrB = IServiceAttribute.GetPropertiesWithAttribute<PropertyAttributeB>();
        Assert.NotNull(withAttrB);
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(PropertyClassB).GetProperty("PropA", UseAllBindingFlags()));
        Assert.Contains(withAttrB, t => t.Item2 ==
            typeof(PropertyClassB).GetProperty("PropB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(PropertyClassB).GetProperty("PropC", UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetField()
    {
        List<(Type, FieldInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetFieldsWithAttribute(typeof(FieldAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldC), UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetFieldsWithAttribute(typeof(FieldAttributeB));
        Assert.NotNull(withAttr);
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(FieldClassB).GetField("FieldA", UseAllBindingFlags()));
        Assert.Contains(withAttr, t => t.Item2 ==
            typeof(FieldClassB).GetField("FieldB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item2 ==
            typeof(FieldClassB).GetField("FieldC", UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetField_Generic()
    {
        List<(Type, FieldInfo, FieldAttributeA)> withAttrA = IServiceAttribute.GetFieldsWithAttribute<FieldAttributeA>();
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item2 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldB), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item2 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldC), UseAllBindingFlags()));

        List<(Type, FieldInfo, FieldAttributeB)> withAttrB = IServiceAttribute.GetFieldsWithAttribute<FieldAttributeB>();
        Assert.NotNull(withAttrB);
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(FieldClassB).GetField("FieldA", UseAllBindingFlags()));
        Assert.Contains(withAttrB, t => t.Item2 ==
            typeof(FieldClassB).GetField("FieldB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item2 ==
            typeof(FieldClassB).GetField("FieldC", UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetMethod_SpecificClass()
    {
        List<(MethodInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetMethodsWithAttribute(typeof(MethodClassA), typeof(MethodAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(MethodClassB).GetMethod("MethodA", UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetMethodsWithAttribute(typeof(MethodClassB), typeof(MethodAttributeB));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(MethodClassB).GetMethod("MethodB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodB), UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetMethod_SpecificClass_Generic()
    {
        List<(MethodInfo, MethodAttributeA)> withAttrA = IServiceAttribute.GetMethodsWithAttribute<MethodAttributeA>(typeof(MethodClassA));
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item1 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item1 ==
            typeof(MethodClassB).GetMethod("MethodA", UseAllBindingFlags()));

        List<(MethodInfo, MethodAttributeB)> withAttrB = IServiceAttribute.GetMethodsWithAttribute<MethodAttributeB>(typeof(MethodClassB));
        Assert.NotNull(withAttrB);
        Assert.Contains(withAttrB, t => t.Item1 ==
            typeof(MethodClassB).GetMethod("MethodB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item1 ==
            typeof(MethodClassA).GetMethod(nameof(MethodClassA.MethodB), UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetProperty_SpecificClass()
    {
        List<(PropertyInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetPropertiesWithAttribute(typeof(PropertyClassA), typeof(PropertyAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(PropertyClassB).GetProperty("PropA", UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetPropertiesWithAttribute(typeof(PropertyClassB), typeof(PropertyAttributeB));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(PropertyClassB).GetProperty("PropB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropB), UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetProperty_SpecificClass_Generic()
    {
        List<(PropertyInfo, PropertyAttributeA)> withAttrA = IServiceAttribute.GetPropertiesWithAttribute<PropertyAttributeA>(typeof(PropertyClassA));
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item1 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item1 ==
            typeof(PropertyClassB).GetProperty("PropA", UseAllBindingFlags()));

        List<(PropertyInfo, PropertyAttributeB)> withAttrB = IServiceAttribute.GetPropertiesWithAttribute<PropertyAttributeB>(typeof(PropertyClassB));
        Assert.NotNull(withAttrB);
        Assert.Contains(withAttrB, t => t.Item1 ==
            typeof(PropertyClassB).GetProperty("PropB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item1 ==
            typeof(PropertyClassA).GetProperty(nameof(PropertyClassA.PropB), UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetField_SpecificClass()
    {
        List<(FieldInfo, IServiceAttribute)> withAttr = IServiceAttribute.GetFieldsWithAttribute(typeof(FieldClassA), typeof(FieldAttributeA));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(FieldClassB).GetField("FieldA", UseAllBindingFlags()));

        withAttr = IServiceAttribute.GetFieldsWithAttribute(typeof(FieldClassB), typeof(FieldAttributeB));
        Assert.NotNull(withAttr);
        Assert.Contains(withAttr, t => t.Item1 ==
            typeof(FieldClassB).GetField("FieldB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttr, t => t.Item1 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldB), UseAllBindingFlags()));
    }

    [Fact]
    public void Attributes_GetField_SpecificClass_Generic()
    {
        List<(FieldInfo, FieldAttributeA)> withAttrA = IServiceAttribute.GetFieldsWithAttribute<FieldAttributeA>(typeof(FieldClassA));
        Assert.NotNull(withAttrA);
        Assert.Contains(withAttrA, t => t.Item1 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldA), UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrA, t => t.Item1 ==
            typeof(FieldClassB).GetField("FieldA", UseAllBindingFlags()));

        List<(FieldInfo, FieldAttributeB)> withAttrB = IServiceAttribute.GetFieldsWithAttribute<FieldAttributeB>(typeof(FieldClassB));
        Assert.NotNull(withAttrB);
        Assert.Contains(withAttrB, t => t.Item1 ==
            typeof(FieldClassB).GetField("FieldB", UseAllBindingFlags()));
        Assert.DoesNotContain(withAttrB, t => t.Item1 ==
            typeof(FieldClassA).GetField(nameof(FieldClassA.FieldB), UseAllBindingFlags()));
    }
}
