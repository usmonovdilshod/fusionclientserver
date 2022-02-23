using Stl.Fusion.Extensions;

namespace FusionBlog.Abstractions;

public record AddOrUpdatePostCommand(Session? Session, Post? Post) : ISessionCommand<Post>
{
    public AddOrUpdatePostCommand() : this(null!, default!)
    {
    }

}

public record RemovePostCommand(Session? Session, string? Slug) : ISessionCommand<Post>
{
    public RemovePostCommand() : this(Session.Null, "")
    {
    }
}

public record Post(string? Title, DateTime Time, string? Content, string? Image, string? Slug)
{
    public Post() : this(null!, default, null!, null!, null!)
    {
    }

    public Post(string? Title) : this(Title, default, null!, null!, null!)
    {
    }
    public Post(string Title, DateTime Time, string Content, string? Image) : this(Title, Time, Content, Image,null!)
    {
    }


}

public interface IPostService
{
    #region Commands

    [CommandHandler]
    Task<Post> AddOrUpdate(AddOrUpdatePostCommand postCommand, CancellationToken cancellationToken = default);

    [CommandHandler]
    Task Remove(RemovePostCommand command, CancellationToken cancellationToken = default);

    #endregion

    #region Queries

    [ComputeMethod]
    Task<int> GetPostCount(Session session, CancellationToken cancellationToken = default);

    [ComputeMethod]
    Task<Post?> Get(Session session, string slug, CancellationToken cancellationToken = default);

    [ComputeMethod]
    Task<Post[]> List(Session session, PageRef<string> pageRef, CancellationToken cancellationToken = default);

    #endregion
}