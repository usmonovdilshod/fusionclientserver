using System.ComponentModel.DataAnnotations;
using FusionBlog.Abstractions;
using Microsoft.EntityFrameworkCore;
using Stl.Fusion.Extensions;

namespace FusionBlog.Services;

public class PostService : IPostService
{
    private readonly ISandboxedKeyValueStore _store;
    private readonly IAuth _auth;

    public PostService(ISandboxedKeyValueStore store, IAuth auth)
    {
        _store = store;
        _auth = auth;
    }
    
    

    public virtual async Task<Post> AddOrUpdate(AddOrUpdatePostCommand postCommand, CancellationToken cancellationToken = default)
    {
        if (Computed.IsInvalidating()) return default!;
        var (session, post) = postCommand;
        var user = await _auth.GetUser(session, cancellationToken);
        user.MustBeAuthenticated();

        //throw new ValidationException("eee yozmagin");
        if (!string.IsNullOrEmpty(post.Title))
            post = post with { Slug = SlugHelper.GenerateSlug(post.Title)};
        else
            await Get(session, post.Slug, cancellationToken);

        if (post.Title.Contains("@"))
            throw new ValidationException("Todo title can't contain '@' symbol.");

        var key = GetPostKey(user, post.Slug);
        await _store.Set(session, key, post, cancellationToken);

        if (post.Title.Contains("#"))
            throw new DbUpdateConcurrencyException(
                "Simulated concurrency conflict. " +
                "Check the log to see if OperationReprocessor retried the command 3 times.");

        return post;
    }

    public virtual async Task Remove(RemovePostCommand command, CancellationToken cancellationToken = default)
    {
        if (Computed.IsInvalidating()) return;
        var (session, id) = command;
        var user = await _auth.GetUser(session, cancellationToken);
        user.MustBeAuthenticated();

        var key = GetPostKey(user, id);
        await _store.Remove(session, key, cancellationToken);
    }

    public virtual async Task<int> GetPostCount(Session session, CancellationToken cancellationToken)
    {
        var user = await _auth.GetUser(session, cancellationToken);
        user.MustBeAuthenticated();

        var count = await _store.Count(session, GetPostKeyPrefix(user), cancellationToken);
        return count;
    }

    public virtual async Task<Post?> Get(Session session, string slug, CancellationToken cancellationToken = default)
    {
        var user = await _auth.GetUser(session, cancellationToken);
        user.MustBeAuthenticated();

        var key = GetPostKey(user, slug);
        return await _store.Get<Post>(session, key, cancellationToken);
    }

    public virtual async Task<Post[]> List(Session session, PageRef<string> pageRef,
        CancellationToken cancellationToken = default)
    {
        var user = await _auth.GetUser(session, cancellationToken);
        user.MustBeAuthenticated();

        var keyPrefix = GetPostKeyPrefix(user);
        var keySuffixes = await _store.ListKeySuffixes(session, keyPrefix, pageRef, cancellationToken);
        var tasks = keySuffixes.Select(suffix =>
            _store.Get<Post>(session, keyPrefix + suffix, cancellationToken).AsTask());
        var todos = await Task.WhenAll(tasks);
        return todos.Where(todo => todo != null).ToArray()!;
    }

    // Private methods

    private string GetPostKey(User user, string id)
        => $"{GetPostKeyPrefix(user)}/{id}";

    private string GetPostKeyPrefix(User user)
        => $"@user/{user.Id}/todo/items";
}