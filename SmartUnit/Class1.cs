using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace SmartUnit
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AssertionAttribute : Attribute
    {
        public string? Name { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class AssertionSetAttribute : Attribute
    {
        public Type AssertionSetType { get; }

        public AssertionSetAttribute(Type assertionSetType)
        {
            if (assertionSetType.BaseType != typeof(AssertionSet))
            {
                throw new ArgumentException(null, nameof(assertionSetType));
            }

            AssertionSetType = assertionSetType;
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class CallbackAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class SkipAttribute : Attribute
    {
        public string? Reason { get; set; }
    }

    public abstract class AssertionSet : ServiceCollection
    {
        public abstract void Configure();
    }

    internal class AssertionException : Exception
    {
        internal AssertionException(string? message) : base(message) { }
    }

    public static class AssertExtensions
    {
        public static T AssertThat<T>(this T obj, Func<T, bool> assertion, string? failureMessage = null)
        {
            if (assertion(obj))
            {
                return obj;
            }

            throw new AssertionException(failureMessage);
        }

        public static async Task<T> AssertThatAsync<T>(this T obj, Func<T, Task<bool>> assertion, string? failureMessage = null)
        {
            if (await assertion(obj))
            {
                return obj;
            }

            throw new AssertionException(failureMessage);
        }

        public static T AssertException<T, TException>(this T obj, Action<T> assertion, string? failureMessage = null) where TException : Exception
        {
            try
            {
                assertion(obj);
            }
            catch (TException)
            {
                return obj;
            }

            throw new AssertionException(failureMessage);
        }

        public static async Task<T> AssertExceptionAsync<T, TException>(this T obj, Func<T, Task> assertion, string? failureMessage = null) where TException : Exception
        {
            try
            {
                await assertion(obj);
            }
            catch (TException)
            {
                return obj;
            }

            throw new AssertionException(failureMessage);
        }
    }
}
