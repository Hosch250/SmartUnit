# SmartUnit
SmartUnit is a modern unit testing framework designed around .NET that provides powerful dependency injection at the class or method level. Simply create a class library, reference `Microsoft.NET.Test.Sdk` (this tells VS it's a test project and to look for test adapters), `Microsoft.Extensions.DependencyInjection` (needed to configure dependency injection; not needed if you don't use that feature), `SmartUnit`, and `SmartUnit.Adapter`.

## Usage
To declare a simple test, add the `Assertion` attribute to a method.
```
[Assertion]
public void MyTest() {}
```

The `Assertion` attribute takes an optional `Name` parameter to override the display name in your test list: `[Assertion(Name = "My Test")]`.

### Dependency Injection
To inject a dependency into a parent class or test, you need an `AssertionSet`.
 ```
public interface IBar {}
public class Bar : IBar {}
public class Foo
{
    public Foo(IBar bar) {}
}

public class AssertionConfiguration : AssertionSet
{
    public override void Configure()
    {
        this.AddSingleton<Foo>();
        this.AddSingleton<IBar, Bar>();
    }
}
```

Reference this assertion set on the test class or test method with `[AssertionSet(typeof(AssertionConfiguration))]`. The attribute on the method overrides the one on the class to enable grouping similar tests with different configurations.

Next, inject and use data into either the parent class or the test method. If there is an exception when resolving types (i.e. you register a type, but not a dependency of that type), the test will throw an exception. If a non-interface type is not found by the dependency resolver, you will get a `null`.

```
[Assertion]
public void MyTest(Foo foo, IBar bar) {}
```

If you inject an interface you haven't registered with the assertion set, you will get a mocked object injected instead. This is the default behavior if you do not provide an `[AssertionSet]` attribute; non-interface parameters and non-callback parameters (see Theories) will receive `null`.

### Theories
SmartUnit approaches theories very differently from most test frameworks. To define a theory, apply the `[Assertion]` attribute to nested methods and add the `[Callback]` attribute to a parameter of the same type as that method (`Action`, a generic `Action<>`, or a `Func<>`). If a parameter has the `[Callback]` attribute, it will not be resolved from any registered dependencies. An `[AssertionSet]` attribute is not needed to inject a callback parameter.
```
public void MyTest([Callback] Action action)
{
    action();

    [Assertion]
    public void Foo() {}

    [Assertion]
    public void Bar() {}
}
```

### Async Tests
Every test is awaited and can run asynchronously. This limits your return types to `void` and task-like types, but also allows any test to use `async` and `await` without drama, as is necessary testing modern code. 

### Skippable Tests
To skip a test, apply the `[Skip]` attribute; this attribute takes an optional `Reason` parameter.
```
[Skip(Reason = "Doesn't work because ...")]
[Assertion]
public void MyTest() {}
```

### Top-level Tests
SmartUnit allows top-level test methods, with the exception of theories. See the Limitations section below for details.

## Assertions
SmartUnit takes a minimalistic approach to assertions with just four extension methods on `object`:
```
obj.AssertThat<MyType>(o => o == 1);
obj.AssertThatAsync<MyType>(async o => (await o.GetSomething()) == 1);
obj.AssertException<MyType, Exception>(o => o.ThrowException());
obj.AssertExceptionAsync<MyType, Exception>(async o => await o.ThrowException());
```

Each of these messages takes an optional message to include as the exception message. Any uncaught exception will fail a test; that is, any exception thrown by the code under test other than an exception inside an `AssertException` call expecting that exception type, so if you prefer a different assertion system, such as the Xunit assertion library, you are free to use it.

## Limitations
Each test is run in a new instance of its containing class. This prevents accidental shared state between tests and allows tests to override the assertion set used. It also prevents intentional shared state between tests, but that can be overcome by defining and injecting a singleton of state.

Test names must be unique; otherwise, the adapter won't be able to find a match, as it only looks methods up by name. You can't have `Foo(IFoo foo, Bar bar)` and `Foo(Bar bar, IFoo foo)`, either as parent tests or nested theory tests. The test discoverer will find them, but the runner will crash running them; I will likely write an analyzer to throw errors when this is detected at a later date.

Test method return types must be awaitable. `void`, `Task`, `ValueTask`, and other awaitable types are good. `int` and other non-awaitable types will crash.

Top-level methods cannot be theories. The reason behind this that nested methods are generated as top-level methods with a non-speakable name (something like `<Parent>g__Name|0__0`). All top-level methods are given non-speakable names and treated as if they were a child of a `Main` method, so there is no way for me to identify which parent method they belong to.