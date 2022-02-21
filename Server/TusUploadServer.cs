using FusionBlog.Abstractions;
using System.IO.Pipelines;
using tusdotnet.Stores;

namespace FusionBlog.Services
{
    internal class TusUploadServer : ITusUpload
    {

        public TusUploadServer(TusDiskStore store)
        {
            Store = store;
        }

        public TusDiskStore Store { get; }

        public event Action<string>? UploadProgress;
        public event Action<string>? Completed;
        public event Action<Exception>? HadError;

        public async Task Upload(Stream file, IDictionary<string,string> metadata, CancellationToken cancellationToken)
        {

            bool complete = false;
            string fileid = await Store.CreateFileAsync(file.Length, metadata.Serialize(), cancellationToken);
#pragma warning disable CS4014
            Store.AppendDataAsync(fileid, file, cancellationToken).ContinueWith(c =>
#pragma warning restore CS4014
            {
                complete = true;
                Completed?.Invoke(fileid);
            }, cancellationToken);

            while (!complete)
            {
                await Task.Delay(10, cancellationToken);
                var length = await Store.GetUploadOffsetAsync(fileid, cancellationToken);
                UploadProgress?.Invoke(((double)length / (double)file.Length) * 100 + "");

            }

        }

    }
}
