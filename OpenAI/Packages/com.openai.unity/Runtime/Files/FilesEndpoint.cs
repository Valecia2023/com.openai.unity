﻿// Licensed under the MIT License. See LICENSE in the project root for license information.

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;
using Utilities.WebRequestRest;

namespace OpenAI.Files
{
    /// <summary>
    /// Files are used to upload documents that can be used with features like Fine-tuning.<br/>
    /// <see href="https://platform.openai.com/docs/api-reference/files"/>
    /// </summary>
    public sealed class FilesEndpoint : OpenAIBaseEndpoint
    {
        [Preserve]
        private class FilesList
        {
            [Preserve]
            [JsonConstructor]
            public FilesList([JsonProperty("data")] List<FileResponse> data)
            {
                Files = data;
            }

            [Preserve]
            [JsonProperty("data")]
            public List<FileResponse> Files { get; }
        }

        internal FilesEndpoint(OpenAIClient client) : base(client) { }

        protected override string Root => "files";

        /// <summary>
        /// Returns a list of files that belong to the user's organization.
        /// </summary>
        /// <returns>List of <see cref="FileResponse"/>.</returns>
        public async Task<IReadOnlyList<FileResponse>> ListFilesAsync(CancellationToken cancellationToken = default)
        {
            var response = await Rest.GetAsync(GetUrl(), new RestParameters(client.DefaultRequestHeaders), cancellationToken);
            response.Validate(EnableDebug);
            return JsonConvert.DeserializeObject<FilesList>(response.Body, OpenAIClient.JsonSerializationOptions)?.Files;
        }

        /// <summary>
        /// Upload a file that contains document(s) to be used across various endpoints/features.
        /// Currently, the size of all the files uploaded by one organization can be up to 1 GB.
        /// Please contact us if you need to increase the storage limit.
        /// </summary>
        /// <param name="filePath">
        /// Local file path to upload.
        /// </param>
        /// <param name="purpose">
        /// The intended purpose of the uploaded documents.
        /// If the purpose is set to "fine-tune", each line is a JSON record with "prompt" and "completion"
        /// fields representing your training examples.
        /// </param>
        /// <param name="uploadProgress">Optional, <see cref="IProgress{T}"/>.</param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns><see cref="FileResponse"/>.</returns>
        public async Task<FileResponse> UploadFileAsync(string filePath, string purpose, IProgress<Progress> uploadProgress = null, CancellationToken cancellationToken = default)
            => await UploadFileAsync(new FileUploadRequest(filePath, purpose), uploadProgress, cancellationToken);

        /// <summary>
        /// Upload a file that contains document(s) to be used across various endpoints/features.
        /// Currently, the size of all the files uploaded by one organization can be up to 1 GB.
        /// Please contact us if you need to increase the storage limit.
        /// </summary>
        /// <param name="request"><see cref="FileUploadRequest"/>.</param>
        /// <param name="uploadProgress">Optional, <see cref="IProgress{T}"/>.</param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns><see cref="FileResponse"/>.</returns>
        public async Task<FileResponse> UploadFileAsync(FileUploadRequest request, IProgress<Progress> uploadProgress = null, CancellationToken cancellationToken = default)
        {
            using var fileData = new MemoryStream();
            var content = new WWWForm();
            await request.File.CopyToAsync(fileData, cancellationToken);
            content.AddField("purpose", request.Purpose);
            content.AddBinaryData("file", fileData.ToArray(), request.FileName);
            request.Dispose();
            var response = await Rest.PostAsync(GetUrl(), content, new RestParameters(client.DefaultRequestHeaders, uploadProgress), cancellationToken);
            response.Validate(EnableDebug);
            return JsonConvert.DeserializeObject<FileResponse>(response.Body, OpenAIClient.JsonSerializationOptions);
        }

        /// <summary>
        /// Delete a file.
        /// </summary>
        /// <param name="fileId">The ID of the file to use for this request</param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns>True, if file was successfully deleted.</returns>
        public async Task<bool> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            return await InternalDeleteFileAsync(1);

            async Task<bool> InternalDeleteFileAsync(int attempt)
            {
                var file = await GetFileInfoAsync(fileId, cancellationToken);
                var response = await Rest.DeleteAsync(GetUrl($"/{file.Id}"), new RestParameters(client.DefaultRequestHeaders), cancellationToken);

                if (!response.Successful)
                {
                    const string fileProcessing = "File is still processing. Check back later.";

                    if (response.Code == 409 ||
                        response.Body != null &&
                        response.Body.Contains(fileProcessing))
                    {
                        // back off requests on each attempt
                        await Task.Delay(1000 * attempt++, cancellationToken);
                        return await InternalDeleteFileAsync(attempt);
                    }
                }

                response.Validate(EnableDebug);
                return JsonConvert.DeserializeObject<DeletedResponse>(response.Body!, OpenAIClient.JsonSerializationOptions)?.Deleted ?? false;
            }
        }

        /// <summary>
        /// Returns information about a specific file.
        /// </summary>
        /// <param name="fileId">The ID of the file to use for this request.</param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns><see cref="FileResponse"/>.</returns>
        public async Task<FileResponse> GetFileInfoAsync(string fileId, CancellationToken cancellationToken = default)
        {
            var response = await Rest.GetAsync(GetUrl($"/{fileId}"), new RestParameters(client.DefaultRequestHeaders), cancellationToken);
            response.Validate(EnableDebug);
            return JsonConvert.DeserializeObject<FileResponse>(response.Body, OpenAIClient.JsonSerializationOptions);
        }

        /// <summary>
        /// Downloads the specified file.
        /// </summary>
        /// <param name="fileId">The file id to download.</param>
        /// <param name="progress">Optional, progress callback.</param>
        /// <param name="cancellationToken">Optional, <see cref="CancellationToken"/>.</param>
        /// <returns>The path to the downloaded file.</returns>
        public async Task<string> DownloadFileAsync(string fileId, IProgress<Progress> progress = null, CancellationToken cancellationToken = default)
        {
            var file = await GetFileInfoAsync(fileId, cancellationToken);
            return await Rest.DownloadFileAsync(GetUrl($"/{file.Id}/content"), file.FileName, new RestParameters(client.DefaultRequestHeaders, progress), EnableDebug, cancellationToken);
        }
    }
}
