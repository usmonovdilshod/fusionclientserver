using System.Globalization;
using BirdMessenger;
using BirdMessenger.Collections;
using BirdMessenger.Infrastructure;
using FusionBlog.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Stl.Fusion.Blazor;

namespace FusionBlog.UI.Tus
{
    public class TusUploadHelper : IDisposable
    {
        private readonly NavigationManager _navigatorManager;
        private readonly ITusClient _tusClient;
        private readonly ITusUpload _tusUpload;


#pragma warning disable CS0649
        public event Action<string>? UploadProgress;
#pragma warning restore CS0649
#pragma warning disable CS0169
        public event Action<string>? Completed;
#pragma warning restore CS0169
        public event Action<Exception>? HadError;

        public TusUploadHelper(IServiceProvider serviceCollection, NavigationManager navigatorManager)
        {
            _navigatorManager = navigatorManager;
            if (BlazorModeHelper.IsServerSideBlazor)
            {
                _tusUpload = serviceCollection.GetRequiredService<ITusUpload>();
            }
            else
            {
                _tusClient = TusBuild.DefaultTusClientBuild(new Uri($"{_navigatorManager.BaseUri}files/")).Build();
            }


        }

        public async Task Upload(Stream file, MetadataCollection metadata, CancellationToken cancelToken)
        {

            #region ServerSide
            if (BlazorModeHelper.IsServerSideBlazor)
            {
                _tusUpload.UploadProgress += HandleProgress;
                _tusUpload.Completed += HandleCompleted;
                _tusUpload.HadError += HandleError;
                await _tusUpload.Upload(file, metadata, cancelToken);
            }
            #endregion
            #region ClientSide
            else
            {

                _tusClient.UploadProgress += HandleProgress;
                _tusClient.UploadFinish += HandleCompleted;

                try
                {
                    var fileUrl = await _tusClient.Create(file.Length, metadata, cancelToken);
                    await _tusClient.Upload(fileUrl, file, cancelToken, cancelToken);
                }

                catch (System.Exception ex)
                {
                    HadError?.Invoke(ex);
                }
            }
            #endregion
        }


        public void Dispose()
        {
            if (BlazorModeHelper.IsServerSideBlazor)
            {
                _tusUpload.UploadProgress -= HandleProgress;
                _tusUpload.Completed -= HandleCompleted;
                _tusUpload.HadError -= HandleError;
            }
            else
            {
                _tusClient.UploadProgress -= HandleCompleted;
                _tusClient.UploadFinish -= HandleCompleted;
            }

        }

        #region Handlers
        private void HandleProgress(string progress)
        {
            UploadProgress?.Invoke(progress);
        }

        private void HandleProgress(ITusClient s, ITusUploadContext e)
        {
            var percent = (e.UploadPercentage * 100).ToString(CultureInfo.InvariantCulture);
            UploadProgress?.Invoke(percent);
        }

        private void HandleCompleted(string fileId)
        {
            Completed?.Invoke($"{_navigatorManager.BaseUri}files/{fileId}");
        }

        private void HandleCompleted(ITusClient s, ITusUploadContext e)
        {
            
            Completed?.Invoke(e.UploadUrl.AbsoluteUri);
        }

        private void HandleError(Exception error)
        {
            HadError?.Invoke(error);
        }


        #endregion

    }
}
