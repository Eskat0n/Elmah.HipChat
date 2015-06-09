namespace Elmah.HipChat
{
    using System;
    using System.Collections;
    using System.Configuration;
    using System.IO;
    using System.Net;
    using System.Text;
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
        }

        protected virtual void OnError(object sender, EventArgs args)
        {
            var httpApplication = (HttpApplication) sender;
            LogException(httpApplication.Server.GetLastError(), httpApplication.Context);
        }

        protected virtual void OnErrorSignaled(object sender, ErrorSignalEventArgs args)
        {
            LogException(args.Exception, args.Context);
        }

        protected virtual void LogException(Exception exception, HttpContext httpContext)
        {
            if (exception == null)
                throw new ArgumentNullException(nameof(exception));

            var args = new ExceptionFilterEventArgs(exception, httpContext);
            OnFiltering(args);
            if (args.Dismissed)
                return;

            var webClient = new WebClient();
            var apiEndpoint = CreateApiEndpoint(AuthToken, RoomId);
            var chatMessage = CreateChatMessage(exception, httpContext, Notify);

            webClient.Headers.Add("Content-Type", "application/json");
            webClient.UploadString(apiEndpoint, chatMessage);
        }

        protected virtual string CreateApiEndpoint(string authToken, string roomId)
        {
            return $"https://api.hipchat.com/v2/room/{roomId}/notification?auth_token={authToken}";
        }

        protected virtual string CreateChatMessage(Exception exception, HttpContext httpContext, bool notify)
        {
            var stringBuilder = new StringBuilder();
            var textWriter = new StringWriter(stringBuilder);
            var jsonWriter = new JsonTextWriter(textWriter);

            jsonWriter.Object();

            jsonWriter.Member("color");
            jsonWriter.String("red");

            jsonWriter.Member("message");
            jsonWriter.String($"{exception.GetType()} at {httpContext.Request.Url}: {exception.Message}");

            jsonWriter.Member("notify");
            jsonWriter.Boolean(notify);

            jsonWriter.Member("message_format");
            jsonWriter.String("text");

            jsonWriter.EndObject();

            return stringBuilder.ToString(); 
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