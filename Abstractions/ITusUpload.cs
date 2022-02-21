namespace FusionBlog.Abstractions
{

    public interface ITusUpload
    {
        Task Upload(Stream file, IDictionary<string,string> metadata, CancellationToken cancellationToken);
        public event Action<string>? UploadProgress;
        public event Action<string>? Completed;
        public event Action<Exception>? HadError;
    }


}
