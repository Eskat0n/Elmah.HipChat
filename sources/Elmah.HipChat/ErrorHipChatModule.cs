namespace Elmah.HipChat
{
    using System;
    using System.Collections;
    using System.Configuration;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Web;

    public class ErrorHipChatModule : HttpModuleBase, IExceptionFiltering
    {
        private const string SectionGroupName = "elmah";
        private const string SubSectionName = "errorHipChat";

        protected override bool SupportDiscoverability => true;

        public event ExceptionFilterEventHandler Filtering;

        protected string AuthToken { get; private set; }
        protected string RoomId { get; private set; }
        protected bool Notify { get; private set; }
        protected bool Async { get; private set; }

        protected override void OnInit(HttpApplication application)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));

            var config = (IDictionary) GetConfig();
            if (config == null)
                return;

            application.Error += OnError;
            ErrorSignal.Get(application).Raised += OnErrorSignaled;

            AuthToken = GetSetting(config, "authToken");
            RoomId = GetSetting(config, "roomId");
            Notify = Convert.ToBoolean(GetSetting(config, "notify", "false"));
            Async = Convert.ToBoolean(GetSetting(config, "async", "false"));
        }

        protected virtual void OnError(object sender, EventArgs args)
        {
            var httpApplication = (HttpApplication) sender;
            OnError(httpApplication.Server.GetLastError(), httpApplication.Context);
        }

        protected virtual void OnErrorSignaled(object sender, ErrorSignalEventArgs args)
        {
            OnError(args.Exception, args.Context);
        }

        protected virtual void OnError(Exception exception, HttpContext httpContext)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            var args = new ExceptionFilterEventArgs(exception, httpContext);
            OnFiltering(args);
            if (args.Dismissed)
                return;

            var error = new Error(exception, httpContext)
            {
                Detail = httpContext.Request.Url.ToString()
            };

            if (Async)
                SendMessageAsync(error);
            else
                SendMessage(error);
        }

        protected virtual void OnFiltering(ExceptionFilterEventArgs args)
        {
            var filterEventHandler = Filtering;
            filterEventHandler?.Invoke(this, args);
        }

        protected virtual object GetConfig()
        {
            return ConfigurationManager.GetSection($"{SectionGroupName}/{SubSectionName}");
        }

        protected virtual void SendMessageAsync(Error error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            ThreadPool.QueueUserWorkItem(SendMessage, error);
        }

        protected virtual void SendMessage(object state)
        {
            SendMessage((Error) state);
        }

        protected virtual void SendMessage(Error error)
        {
            var webClient = new WebClient();
            var apiEndpoint = CreateApiEndpoint(AuthToken, RoomId);
            var chatMessage = CreateChatMessage(error, Notify);

            webClient.Headers.Add("Content-Type", "application/json");
            webClient.UploadString(apiEndpoint, chatMessage);
        }

        protected virtual string CreateApiEndpoint(string authToken, string roomId)
        {
            return $"https://api.hipchat.com/v2/room/{roomId}/notification?auth_token={authToken}";
        }

        protected virtual string CreateChatMessage(Error error, bool notify)
        {
            var stringBuilder = new StringBuilder();
            var textWriter = new StringWriter(stringBuilder);
            var jsonWriter = new JsonTextWriter(textWriter);

            jsonWriter.Object();

            jsonWriter.Member("color");
            jsonWriter.String("red");

            jsonWriter.Member("message");            
            jsonWriter.String($"{error.Exception.GetType()} at <a href='{error.Detail}' target='_blank'>{error.Detail}</a>: {error.Exception.Message}");

            jsonWriter.Member("notify");
            jsonWriter.Boolean(notify);

            jsonWriter.Member("message_format");
            jsonWriter.String("html");

            jsonWriter.EndObject();

            return stringBuilder.ToString(); 
        }

        private static string GetSetting(IDictionary config, string name, string defaultValue = null)
        {
            var setting = (string) config[name];

            if (string.IsNullOrEmpty(setting) == false)
                return setting;

            if (defaultValue == null)
                throw new ApplicationException($"The required configuration setting \"{name}\" is missing for the error mailing module.");

            return defaultValue;
        }
    }
}