namespace ChatServer.Utils
{
    public static class Constants
    {
        // Heartbeat settings
        public const int HEARTBEAT_INTERVAL_SECONDS = 15;
        public const int HEARTBEAT_TIMEOUT_SECONDS = 45;
        
        // Reconnect settings
        public const int MAX_RECONNECT_ATTEMPTS = 5;
        public const int RECONNECT_DELAY_MS = 1000;
        
        // Resume settings
        public const int MAX_MESSAGES_PER_CONVERSATION = 50;
        
        // Server settings
        public const int SERVER_PORT = 8888;
        public const int GRACEFUL_SHUTDOWN_DELAY_MS = 2000;
        
        // Message types
        public const string MSG_TYPE_AUTH = "auth";
        public const string MSG_TYPE_HEARTBEAT = "heartbeat";
        public const string MSG_TYPE_RESUME = "resume";
        public const string MSG_TYPE_PRESENCE_UPDATE = "presence_update";
        public const string MSG_TYPE_JOIN_CONVERSATION = "join_conversation";
        public const string MSG_TYPE_GET_PRESENCE = "get_presence";
        public const string MSG_TYPE_SERVER_GOING_DOWN = "server_going_down";
        public const string MSG_TYPE_CONNECTION_CLOSED = "connection_closed";
        public const string MSG_TYPE_MESSAGE_CREATED = "message_created";
        public const string MSG_TYPE_RESUME_SNAPSHOT = "resume_snapshot";
        public const string MSG_TYPE_ERROR = "error";
    }
}