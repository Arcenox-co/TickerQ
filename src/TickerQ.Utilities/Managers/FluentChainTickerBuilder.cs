using System;
using System.Collections.Generic;
using System.Linq;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Enums;

namespace TickerQ.Utilities.Managers
{
    /// <summary>
    /// Fluent chain ticker builder with lambda configuration and duplicate prevention
    /// </summary>
    public class FluentChainTickerBuilder<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        private readonly TTimeTicker _rootTicker;
        private readonly bool[] _childrenUsed = new bool[5]; // Track which children are used
        private readonly bool[][] _grandChildrenUsed = new bool[5][]; // Track which grandchildren are used per child

        private FluentChainTickerBuilder()
        {
            _rootTicker = new TTimeTicker
            {
                Id = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Children = new List<TTimeTicker>()
            };
            
            // Initialize grandchildren tracking
            for (int i = 0; i < 5; i++)
            {
                _grandChildrenUsed[i] = new bool[5];
            }
        }

        /// <summary>
        /// Start building by configuring the parent ticker
        /// </summary>
        public static FluentChainTickerBuilder<TTimeTicker> BeginWith(Action<ParentBuilder<TTimeTicker>> configure)
        {
            var builder = new FluentChainTickerBuilder<TTimeTicker>();
            var parentBuilder = new ParentBuilder<TTimeTicker>(builder._rootTicker);
            configure(parentBuilder);
            return builder;
        }

        /// <summary>
        /// Configure the first child (1/5)
        /// </summary>
        public FirstChildBuilder WithFirstChild(Action<ChildBuilder<TTimeTicker>> configure)
        {
            if (_childrenUsed[0])
                throw new InvalidOperationException("First child has already been configured");
            
            _childrenUsed[0] = true;
            var child = CreateChild();
            var childBuilder = new ChildBuilder<TTimeTicker>(child);
            configure(childBuilder);
            _rootTicker.Children.Add(child);
            
            return new FirstChildBuilder(this, child, 0);
        }

        /// <summary>
        /// Configure the second child (2/5)
        /// </summary>
        public SecondChildBuilder WithSecondChild(Action<ChildBuilder<TTimeTicker>> configure)
        {
            if (_childrenUsed[1])
                throw new InvalidOperationException("Second child has already been configured");
            
            _childrenUsed[1] = true;
            var child = CreateChild();
            var childBuilder = new ChildBuilder<TTimeTicker>(child);
            configure(childBuilder);
            _rootTicker.Children.Add(child);
            
            return new SecondChildBuilder(this, child, 1);
        }

        /// <summary>
        /// Configure the third child (3/5)
        /// </summary>
        public ThirdChildBuilder WithThirdChild(Action<ChildBuilder<TTimeTicker>> configure)
        {
            if (_childrenUsed[2])
                throw new InvalidOperationException("Third child has already been configured");
            
            _childrenUsed[2] = true;
            var child = CreateChild();
            var childBuilder = new ChildBuilder<TTimeTicker>(child);
            configure(childBuilder);
            _rootTicker.Children.Add(child);
            
            return new ThirdChildBuilder(this, child, 2);
        }

        /// <summary>
        /// Configure the fourth child (4/5)
        /// </summary>
        public FourthChildBuilder WithFourthChild(Action<ChildBuilder<TTimeTicker>> configure)
        {
            if (_childrenUsed[3])
                throw new InvalidOperationException("Fourth child has already been configured");
            
            _childrenUsed[3] = true;
            var child = CreateChild();
            var childBuilder = new ChildBuilder<TTimeTicker>(child);
            configure(childBuilder);
            _rootTicker.Children.Add(child);
            
            return new FourthChildBuilder(this, child, 3);
        }

        /// <summary>
        /// Configure the fifth child (5/5)
        /// </summary>
        public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeTicker>> configure)
        {
            if (_childrenUsed[4])
                throw new InvalidOperationException("Fifth child has already been configured");
            
            _childrenUsed[4] = true;
            var child = CreateChild();
            var childBuilder = new ChildBuilder<TTimeTicker>(child);
            configure(childBuilder);
            _rootTicker.Children.Add(child);
            
            return new FifthChildBuilder(this, child, 4);
        }

        private TTimeTicker CreateChild()
        {
            return new TTimeTicker
            {
                Id = Guid.NewGuid(),
                ParentId = _rootTicker.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Children = new List<TTimeTicker>()
            };
        }

        private TTimeTicker CreateGrandChild(TTimeTicker parent)
        {
            return new TTimeTicker
            {
                Id = Guid.NewGuid(),
                ParentId = parent.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Children = new List<TTimeTicker>()
            };
        }

        /// <summary>
        /// Build the final ticker entity
        /// </summary>
        public TTimeTicker Build() => _rootTicker;

        /// <summary>
        /// Implicit conversion to entity
        /// </summary>
        public static implicit operator TTimeTicker(FluentChainTickerBuilder<TTimeTicker> builder) => builder.Build();

        // Individual child builders to prevent duplicate configuration
        public class FirstChildBuilder
        {
            private readonly FluentChainTickerBuilder<TTimeTicker> _mainBuilder;
            private readonly TTimeTicker _child;
            private readonly int _childIndex;

            internal FirstChildBuilder(FluentChainTickerBuilder<TTimeTicker> mainBuilder, TTimeTicker child, int childIndex)
            {
                _mainBuilder = mainBuilder;
                _child = child;
                _childIndex = childIndex;
            }

            public FirstChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][0])
                    throw new InvalidOperationException("First grandchild of first child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FirstChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][1])
                    throw new InvalidOperationException("Second grandchild of first child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FirstChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][2])
                    throw new InvalidOperationException("Third grandchild of first child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FirstChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][3])
                    throw new InvalidOperationException("Fourth grandchild of first child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FirstChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][4])
                    throw new InvalidOperationException("Fifth grandchild of first child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public SecondChildBuilder WithSecondChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithSecondChild(configure);
            public ThirdChildBuilder WithThirdChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithThirdChild(configure);
            public FourthChildBuilder WithFourthChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithFourthChild(configure);
            public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithFifthChild(configure);
            public TTimeTicker Build() => _mainBuilder.Build();
            public static implicit operator TTimeTicker(FirstChildBuilder builder) => builder.Build();
        }

        public class SecondChildBuilder
        {
            private readonly FluentChainTickerBuilder<TTimeTicker> _mainBuilder;
            private readonly TTimeTicker _child;
            private readonly int _childIndex;

            internal SecondChildBuilder(FluentChainTickerBuilder<TTimeTicker> mainBuilder, TTimeTicker child, int childIndex)
            {
                _mainBuilder = mainBuilder;
                _child = child;
                _childIndex = childIndex;
            }

            public SecondChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][0])
                    throw new InvalidOperationException("First grandchild of second child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public SecondChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][1])
                    throw new InvalidOperationException("Second grandchild of second child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public SecondChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][2])
                    throw new InvalidOperationException("Third grandchild of second child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public SecondChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][3])
                    throw new InvalidOperationException("Fourth grandchild of second child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public SecondChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][4])
                    throw new InvalidOperationException("Fifth grandchild of second child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public ThirdChildBuilder WithThirdChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithThirdChild(configure);
            public FourthChildBuilder WithFourthChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithFourthChild(configure);
            public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithFifthChild(configure);
            public TTimeTicker Build() => _mainBuilder.Build();
            public static implicit operator TTimeTicker(SecondChildBuilder builder) => builder.Build();
        }

        public class ThirdChildBuilder
        {
            private readonly FluentChainTickerBuilder<TTimeTicker> _mainBuilder;
            private readonly TTimeTicker _child;
            private readonly int _childIndex;

            internal ThirdChildBuilder(FluentChainTickerBuilder<TTimeTicker> mainBuilder, TTimeTicker child, int childIndex)
            {
                _mainBuilder = mainBuilder;
                _child = child;
                _childIndex = childIndex;
            }

            public ThirdChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][0])
                    throw new InvalidOperationException("First grandchild of third child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public ThirdChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][1])
                    throw new InvalidOperationException("Second grandchild of third child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public ThirdChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][2])
                    throw new InvalidOperationException("Third grandchild of third child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public ThirdChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][3])
                    throw new InvalidOperationException("Fourth grandchild of third child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public ThirdChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][4])
                    throw new InvalidOperationException("Fifth grandchild of third child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FourthChildBuilder WithFourthChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithFourthChild(configure);
            public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithFifthChild(configure);
            public TTimeTicker Build() => _mainBuilder.Build();
            public static implicit operator TTimeTicker(ThirdChildBuilder builder) => builder.Build();
        }

        public class FourthChildBuilder
        {
            private readonly FluentChainTickerBuilder<TTimeTicker> _mainBuilder;
            private readonly TTimeTicker _child;
            private readonly int _childIndex;

            internal FourthChildBuilder(FluentChainTickerBuilder<TTimeTicker> mainBuilder, TTimeTicker child, int childIndex)
            {
                _mainBuilder = mainBuilder;
                _child = child;
                _childIndex = childIndex;
            }

            public FourthChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][0])
                    throw new InvalidOperationException("First grandchild of fourth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FourthChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][1])
                    throw new InvalidOperationException("Second grandchild of fourth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FourthChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][2])
                    throw new InvalidOperationException("Third grandchild of fourth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FourthChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][3])
                    throw new InvalidOperationException("Fourth grandchild of fourth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FourthChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][4])
                    throw new InvalidOperationException("Fifth grandchild of fourth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FifthChildBuilder WithFifthChild(Action<ChildBuilder<TTimeTicker>> configure) => _mainBuilder.WithFifthChild(configure);
            public TTimeTicker Build() => _mainBuilder.Build();
            public static implicit operator TTimeTicker(FourthChildBuilder builder) => builder.Build();
        }

        public class FifthChildBuilder
        {
            private readonly FluentChainTickerBuilder<TTimeTicker> _mainBuilder;
            private readonly TTimeTicker _child;
            private readonly int _childIndex;

            internal FifthChildBuilder(FluentChainTickerBuilder<TTimeTicker> mainBuilder, TTimeTicker child, int childIndex)
            {
                _mainBuilder = mainBuilder;
                _child = child;
                _childIndex = childIndex;
            }

            public FifthChildBuilder WithFirstGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][0])
                    throw new InvalidOperationException("First grandchild of fifth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][0] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FifthChildBuilder WithSecondGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][1])
                    throw new InvalidOperationException("Second grandchild of fifth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][1] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FifthChildBuilder WithThirdGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][2])
                    throw new InvalidOperationException("Third grandchild of fifth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][2] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FifthChildBuilder WithFourthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][3])
                    throw new InvalidOperationException("Fourth grandchild of fifth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][3] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public FifthChildBuilder WithFifthGrandChild(Action<GrandChildBuilder<TTimeTicker>> configure)
            {
                if (_mainBuilder._grandChildrenUsed[_childIndex][4])
                    throw new InvalidOperationException("Fifth grandchild of fifth child has already been configured");
                
                _mainBuilder._grandChildrenUsed[_childIndex][4] = true;
                var grandChild = _mainBuilder.CreateGrandChild(_child);
                var grandChildBuilder = new GrandChildBuilder<TTimeTicker>(grandChild);
                configure(grandChildBuilder);
                _child.Children.Add(grandChild);
                return this;
            }

            public TTimeTicker Build() => _mainBuilder.Build();
            public static implicit operator TTimeTicker(FifthChildBuilder builder) => builder.Build();
        }
    }

    /// <summary>
    /// Parent builder for configuring the root ticker
    /// </summary>
    public class ParentBuilder<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        private readonly TTimeTicker _parent;

        internal ParentBuilder(TTimeTicker parent)
        {
            _parent = parent;
        }

        public ParentBuilder<TTimeTicker> SetFunction(string functionName)
        {
            _parent.Function = functionName;
            return this;
        }

        public ParentBuilder<TTimeTicker> SetDescription(string description)
        {
            _parent.Description = description;
            return this;
        }

        public ParentBuilder<TTimeTicker> SetExecutionTime(DateTime executionTime)
        {
            _parent.ExecutionTime = executionTime;
            return this;
        }

        public ParentBuilder<TTimeTicker> SetRequest(byte[] request)
        {
            _parent.Request = request;
            return this;
        }

        public ParentBuilder<TTimeTicker> SetRetries(int retries, params int[] intervals)
        {
            _parent.Retries = retries;
            _parent.RetryIntervals = intervals;
            return this;
        }
    }

    /// <summary>
    /// Child builder for configuring individual children
    /// </summary>
    public class ChildBuilder<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        private readonly TTimeTicker _child;

        internal ChildBuilder(TTimeTicker child)
        {
            _child = child;
        }

        public ChildBuilder<TTimeTicker> SetFunction(string functionName)
        {
            _child.Function = functionName;
            return this;
        }

        public ChildBuilder<TTimeTicker> SetDescription(string description)
        {
            _child.Description = description;
            return this;
        }

        public ChildBuilder<TTimeTicker> SetRunCondition(RunCondition condition)
        {
            _child.RunCondition = condition;
            return this;
        }

        public ChildBuilder<TTimeTicker> SetExecutionTime(DateTime executionTime)
        {
            _child.ExecutionTime = executionTime;
            return this;
        }

        public ChildBuilder<TTimeTicker> SetRequest(byte[] request)
        {
            _child.Request = request;
            return this;
        }

        public ChildBuilder<TTimeTicker> SetRetries(int retries, params int[] intervals)
        {
            _child.Retries = retries;
            _child.RetryIntervals = intervals;
            return this;
        }
    }

    /// <summary>
    /// Grandchild builder for configuring individual grandchildren
    /// </summary>
    public class GrandChildBuilder<TTimeTicker> where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
    {
        private readonly TTimeTicker _grandChild;

        internal GrandChildBuilder(TTimeTicker grandChild)
        {
            _grandChild = grandChild;
        }

        public GrandChildBuilder<TTimeTicker> SetFunction(string functionName)
        {
            _grandChild.Function = functionName;
            return this;
        }

        public GrandChildBuilder<TTimeTicker> SetDescription(string description)
        {
            _grandChild.Description = description;
            return this;
        }

        public GrandChildBuilder<TTimeTicker> SetRunCondition(RunCondition condition)
        {
            _grandChild.RunCondition = condition;
            return this;
        }

        public GrandChildBuilder<TTimeTicker> SetExecutionTime(DateTime executionTime)
        {
            _grandChild.ExecutionTime = executionTime;
            return this;
        }

        public GrandChildBuilder<TTimeTicker> SetRequest(byte[] request)
        {
            _grandChild.Request = request;
            return this;
        }

        public GrandChildBuilder<TTimeTicker> SetRetries(int retries, params int[] intervals)
        {
            _grandChild.Retries = retries;
            _grandChild.RetryIntervals = intervals;
            return this;
        }
    }

    /// <summary>
    /// Extension methods for easier creation
    /// </summary>
    public static class FluentChainTickerBuilderExtensions
    {
        /// <summary>
        /// Start building a fluent chain ticker by configuring the parent
        /// </summary>
        public static FluentChainTickerBuilder<TTimeTicker> BeginWith<TTimeTicker>(Action<ParentBuilder<TTimeTicker>> configure)
            where TTimeTicker : TimeTickerEntity<TTimeTicker>, new()
            => FluentChainTickerBuilder<TTimeTicker>.BeginWith(configure);
    }
}