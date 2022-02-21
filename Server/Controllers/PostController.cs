using FusionBlog.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Stl.Fusion.Authentication;
using Stl.Fusion.Extensions;
using Stl.Fusion.Server;

namespace FusionBlog.Server.Controllers;

[Route("api/[controller]/[action]")]
[ApiController, JsonifyErrors]
public class PostController : ControllerBase, IPostService
{
    #region Constructor

    private readonly IPostService _posts;
    private readonly ISessionResolver _sessionResolver;

    public PostController(IPostService posts, ISessionResolver sessionResolver)
    {
        _posts = posts;
        _sessionResolver = sessionResolver;
    }

    #endregion

    #region Commands

    [HttpPost,Publish]
    public Task<Post> AddOrUpdate(AddOrUpdatePostCommand postCommand,
        CancellationToken cancellationToken = default) =>
        _posts.AddOrUpdate(postCommand.UseDefaultSession(_sessionResolver), cancellationToken);
    [HttpDelete,Publish]
    public Task Remove(RemovePostCommand command, CancellationToken cancellationToken = default) =>
        _posts.Remove(command.UseDefaultSession(_sessionResolver), cancellationToken);

    #endregion

    #region Queries

    [HttpGet,Publish]
    public Task<int> GetPostCount(Session session, CancellationToken cancellationToken = default) =>
        _posts.GetPostCount(session, cancellationToken);

    [HttpGet,Publish]
    public Task<Post?> Get(Session session, string slug, CancellationToken cancellationToken = default) =>
        _posts.Get(session, slug, cancellationToken);

    [HttpGet,Publish]
    public Task<Post[]> List(Session session, PageRef<string> pageRef, CancellationToken cancellationToken = default) =>
        _posts.List(session, pageRef, cancellationToken);

    #endregion
}