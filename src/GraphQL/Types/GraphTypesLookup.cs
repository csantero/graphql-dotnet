using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GraphQL.Introspection;

namespace GraphQL.Types
{
    public class GraphTypesLookup
    {
        private readonly ConcurrentDictionary<string, IGraphType> _types = new ConcurrentDictionary<string, IGraphType>();

        public GraphTypesLookup()
        {
            AddType<StringGraphType>();
            AddType<BooleanGraphType>();
            AddType<FloatGraphType>();
            AddType<IntGraphType>();
            AddType<IdGraphType>();
            AddType<DateGraphType>();
            AddType<DecimalGraphType>();

            AddType<__Schema>();
            AddType<__Type>();
            AddType<__Directive>();
            AddType<__Field>();
            AddType<__EnumValue>();
            AddType<__InputValue>();
            AddType<__TypeKind>();
        }

        public static GraphTypesLookup Create(IEnumerable<IGraphType> types, Func<Type, IGraphType> resolveType)
        {
            var lookup = new GraphTypesLookup();

            var ctx = new TypeCollectionContext(resolveType, (name, graphType, context) =>
            {
                if (lookup[name] == null)
                {
                    lookup.AddType(graphType, context);
                }
            });

            types.Apply(type =>
            {
                lookup.AddType(type, ctx);
            });

            return lookup;
        }

        public void Clear()
        {
            _types.Clear();
        }

        public IEnumerable<IGraphType> All()
        {
            return _types.Select(t => t.Value);
        }

        public IGraphType this[string typeName]
        {
            get
            {
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    throw new ArgumentOutOfRangeException(nameof(typeName), "A type name is required to lookup.");
                }

                IGraphType type;
                var name = typeName.TrimGraphQLTypes();
                _types.TryGetValue(name, out type);
                return type;
            }
            set
            {
                _types.AddOrUpdate(typeName.TrimGraphQLTypes(), value, (n, t) => value);
            }
        }

        public IGraphType this[Type type]
        {
            get
            {
                var result = _types.FirstOrDefault(x => x.Value.GetType() == type);
                return result.Value;
            }
        }

        public IEnumerable<IGraphType> FindImplemenationsOf(Type type)
        {
            return _types
                .Select(t => t.Value)
                .Where(t => t is IImplementInterfaces && t.As<IImplementInterfaces>().Interfaces.Any(i => i == type))
                .Select(x => x)
                .ToList();
        }

        public void AddType<TType>()
            where TType : IGraphType, new()
        {
            var context = new TypeCollectionContext(
                type => (GraphType) Activator.CreateInstance(type),
                (name, type, _) =>
                {
                    var trimmed = name.TrimGraphQLTypes();
                    _types.AddOrUpdate(trimmed, type, (n, t) => type);
                    _?.AddType(trimmed, type, null);
                });

            AddType<TType>(context);
        }

        public void AddType<TType>(TypeCollectionContext context)
            where TType : IGraphType
        {
            var type = typeof(TType).GetNamedType();
            var instance = context.ResolveType(type);
            AddType(instance, context);
        }

        public void AddType(IGraphType type, TypeCollectionContext context)
        {
            if (type == null)
            {
                return;
            }

            if (type is NonNullGraphType || type is ListGraphType)
            {
                throw new ExecutionError("Only add root types.");
            }

            var name = type.CollectTypes(context).TrimGraphQLTypes();
            _types.AddOrUpdate(name, type, (n, t) => type);

            if (type is IComplexGraphType)
            {
                var complexType = type as IComplexGraphType;
                complexType.Fields.Apply(field =>
                {
                    AddTypeIfNotRegistered(field.Type, context);

                    field.Arguments?.Apply(arg =>
                    {
                        AddTypeIfNotRegistered(arg.Type, context);
                    });
                });
            }

            if (type is IObjectGraphType)
            {
                var obj = (IObjectGraphType) type;
                obj.Interfaces.Apply(objectInterface =>
                {
                    AddTypeIfNotRegistered(objectInterface, context);

                    var interfaceInstance = this[objectInterface] as IInterfaceGraphType;
                    if (interfaceInstance != null)
                    {
                        interfaceInstance.AddPossibleType(obj);

                        if (interfaceInstance.ResolveType == null && obj.IsTypeOf == null)
                        {
                            throw new ExecutionError((
                                "Interface type {0} does not provide a \"resolveType\" function " +
                                "and possible Type \"{1}\" does not provide a \"isTypeOf\" function.  " +
                                "There is no way to resolve this possible type during execution.")
                                .ToFormat(interfaceInstance, obj));
                        }
                    }
                });
            }

            if (type is UnionGraphType)
            {
                var union = (UnionGraphType) type;

                if (!union.Types.Any())
                {
                    throw new ExecutionError("Must provide types for Union {0}.".ToFormat(union));
                }

                union.Types.Apply(unionedType =>
                {
                    AddTypeIfNotRegistered(unionedType, context);

                    var objType = this[unionedType] as IObjectGraphType;

                    if (union.ResolveType == null && objType != null && objType.IsTypeOf == null)
                    {
                        throw new ExecutionError((
                            "Union type {0} does not provide a \"resolveType\" function" +
                            "and possible Type \"{1}\" does not provide a \"isTypeOf\" function.  " +
                            "There is no way to resolve this possible type during execution.")
                            .ToFormat(union, objType));
                    }

                    union.AddPossibleType(objType);
                });
            }
        }

        private void AddTypeIfNotRegistered(Type type, TypeCollectionContext context)
        {
            var namedType = type.GetNamedType();
            var foundType = this[namedType];
            if (foundType == null)
            {
                AddType(context.ResolveType(namedType), context);
            }
        }
    }
}
