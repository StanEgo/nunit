﻿// ***********************************************************************
// Copyright (c) 2014 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

using System;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Compatibility;
using NUnit.Framework.Interfaces;

namespace NUnit.Framework.Internal.Execution
{
    using Commands;
    using Interfaces;

    /// <summary>
    /// A utility class to create TestCommands
    /// </summary>
    public static class CommandBuilder
    {
        /// <summary>
        /// Gets the command to be executed before any of
        /// the child tests are run.
        /// </summary>
        /// <returns>A TestCommand</returns>
        /// 
        public static TestCommand MakeOneTimeSetUpCommand(TestSuite suite, List<SetUpTearDownItem> setUpTearDown, List<TestActionItem> actions)
        {
            // Handle skipped tests
            if (suite.RunState != RunState.Runnable && suite.RunState != RunState.Explicit)
                return new SkipCommand(suite);

            TestCommand command = new EmptyTestCommand(suite);

            // Add Action Commands 
            int index = actions.Count;
            while (--index >= 0)
                command = new BeforeTestActionCommand(command, actions[index]);

            if (suite.TypeInfo != null)
            {
                // Build the OneTimeSetUpCommands
                foreach (SetUpTearDownItem item in setUpTearDown)
                    command = new OneTimeSetUpCommand(command, item);

                // Construct the fixture if necessary
                if (!suite.TypeInfo.IsStaticClass)
                    command = new ConstructFixtureCommand(command);
            }

            // Prefix with any IApplyToContext items from attributes
            var changes = suite.GetCustomAttributes<IApplyToContext>(true);
//            IList<IApplyToContext> changes = null;

//            if (suite.TypeInfo != null)
//                changes = suite.TypeInfo.GetCustomAttributes<IApplyToContext>(true);
//            else if (suite.Method != null)
//                changes = suite.Method.GetCustomAttributes<IApplyToContext>(true);
//            else
//            {
//                var testAssembly = suite as TestAssembly;
//                if (testAssembly != null)
//#if PORTABLE || NETSTANDARD1_6
//                    changes = new List<IApplyToContext>(testAssembly.Assembly.GetAttributes<IApplyToContext>());
//#else
//                    changes = (IApplyToContext[])testAssembly.Assembly.GetCustomAttributes(typeof(IApplyToContext), true);
//#endif
//            }

            if (changes != null && changes.Length > 0)
                command = new ApplyChangesToContextCommand(command, changes);

            return command;
        }

        /// <summary>
        /// Gets the command to be executed after all of the
        /// child tests are run.
        /// </summary>
        /// <returns>A TestCommand</returns>
        public static TestCommand MakeOneTimeTearDownCommand(TestSuite suite, List<SetUpTearDownItem> setUpTearDownItems, List<TestActionItem> actions)
        {
            TestCommand command = new EmptyTestCommand(suite);

            // Dispose of fixture if necessary
            if (suite is IDisposableFixture && typeof(IDisposable).IsAssignableFrom(suite.TypeInfo.Type))
                command = new DisposeFixtureCommand(command);

            // Create the OneTimeTearDown commands
            foreach (SetUpTearDownItem item in setUpTearDownItems)
                command = new OneTimeTearDownCommand(command, item);
      
            // Create the AfterTestAction commands
            int index = actions.Count;
            while (--index >= 0)
                command = new AfterTestActionCommand(command, actions[index]);

            // For Theories, follow with TheoryResultCommand to adjust result as needed
            if (suite.TestType == "Theory")
                command = new TheoryResultCommand(command);

            return command;
        }

        /// <summary>
        /// Builds the set up tear down list.
        /// </summary>
        /// <param name="fixtureType">Type of the fixture.</param>
        /// <param name="setUpType">Type of the set up attribute.</param>
        /// <param name="tearDownType">Type of the tear down attribute.</param>
        /// <returns>A list of SetUpTearDownItems</returns>
        public static List<SetUpTearDownItem> BuildSetUpTearDownList(Type fixtureType, Type setUpType, Type tearDownType)
        {
            var setUpMethods = Reflect.GetMethodsWithAttribute(fixtureType, setUpType, true);
            var tearDownMethods = Reflect.GetMethodsWithAttribute(fixtureType, tearDownType, true);

            var list = new List<SetUpTearDownItem>();

            while (fixtureType != null && !fixtureType.Equals(typeof(object)))
            {
                var node = BuildNode(fixtureType, setUpMethods, tearDownMethods);
                if (node.HasMethods)
                    list.Add(node);

                fixtureType = fixtureType.GetTypeInfo().BaseType;
            }

            return list;
        }

        // This method builds a list of nodes that can be used to 
        // run setup and teardown according to the NUnit specs.
        // We need to execute setup and teardown methods one level
        // at a time. However, we can't discover them by reflection
        // one level at a time, because that would cause overridden
        // methods to be called twice, once on the base class and
        // once on the derived class.
        // 
        // For that reason, we start with a list of all setup and
        // teardown methods, found using a single reflection call,
        // and then descend through the inheritance hierarchy,
        // adding each method to the appropriate level as we go.
        private static SetUpTearDownItem BuildNode(Type fixtureType, IList<MethodInfo> setUpMethods, IList<MethodInfo> tearDownMethods)
        {
            // Create lists of methods for this level only.
            // Note that FindAll can't be used because it's not
            // available on all the platforms we support.
            var mySetUpMethods = SelectMethodsByDeclaringType(fixtureType, setUpMethods);
            var myTearDownMethods = SelectMethodsByDeclaringType(fixtureType, tearDownMethods);

            return new SetUpTearDownItem(mySetUpMethods, myTearDownMethods);
        }

        private static List<MethodInfo> SelectMethodsByDeclaringType(Type type, IList<MethodInfo> methods)
        {
            var list = new List<MethodInfo>();

            foreach (var method in methods)
                if (method.DeclaringType == type)
                    list.Add(method);

            return list;
        }
    }
}
