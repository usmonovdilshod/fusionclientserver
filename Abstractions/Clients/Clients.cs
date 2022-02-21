using RestEase;
using Stl.Fusion.Extensions;

namespace FusionBlog.Abstractions.Clients;

[BasePath("post")]
public interface IPostClientDef
{
    [Post(nameof(AddOrUpdate))]
    Task<Post> AddOrUpdate([Body] AddOrUpdatePostCommand command, CancellationToken cancellationToken = default);
    [Post(nameof(Remove))]
    Task Remove([Body] RemovePostCommand command, CancellationToken cancellationToken = default);

    [Get(nameof(Get))]
    Task<Post?> Get(Session session, string id, CancellationToken cancellationToken = default);
    [Get(nameof(List))]
    Task<Post[]> List(Session session, PageRef<string> pageRef, CancellationToken cancellationToken = default);
    [Get(nameof(GetPostCount))]
    Task<int> GetPostCount(Session session, CancellationToken cancellationToken = default);
}
