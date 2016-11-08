namespace GraphQL.Types
{
    public interface IGraphType
    {
        string Name { get; set; }
        string Description { get; set; }
        string DeprecationReason { get; set; }

        string CollectTypes(TypeCollectionContext context);
    }

    public interface IOutputGraphType : IGraphType
    {
    }

    public interface IInputGraphType : IGraphType
    {
    }
}
