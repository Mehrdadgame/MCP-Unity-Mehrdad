namespace UnityMCP.Protocol
{
    /// <summary>
    /// Canonical error codes shared by every handler. The full set from the spec is
    /// declared up front so later phases reference constants instead of magic strings.
    /// </summary>
    public static class ErrorCodes
    {
        // Connection / protocol
        public const string MALFORMED_REQUEST = "MALFORMED_REQUEST";
        public const string UNKNOWN_CATEGORY = "UNKNOWN_CATEGORY";
        public const string UNKNOWN_ACTION = "UNKNOWN_ACTION";
        public const string INVALID_PARAMS = "INVALID_PARAMS";

        // Resolution
        public const string NOT_FOUND = "NOT_FOUND";
        public const string AMBIGUOUS_TARGET = "AMBIGUOUS_TARGET";
        public const string TYPE_NOT_FOUND = "TYPE_NOT_FOUND";
        public const string PROPERTY_NOT_FOUND = "PROPERTY_NOT_FOUND";

        // State
        public const string IN_PLAY_MODE = "IN_PLAY_MODE";
        public const string NOT_IN_PLAY_MODE = "NOT_IN_PLAY_MODE";
        public const string COMPILING = "COMPILING";
        public const string DOMAIN_RELOAD_PENDING = "DOMAIN_RELOAD_PENDING";

        // Permission
        public const string PERMISSION_DENIED = "PERMISSION_DENIED";
        public const string CONFIRMATION_REQUIRED = "CONFIRMATION_REQUIRED";

        // External packages
        public const string TMP_NOT_INSTALLED = "TMP_NOT_INSTALLED";
        public const string CINEMACHINE_NOT_INSTALLED = "CINEMACHINE_NOT_INSTALLED";
        public const string ADDRESSABLES_NOT_INSTALLED = "ADDRESSABLES_NOT_INSTALLED";
        public const string LOCALIZATION_NOT_INSTALLED = "LOCALIZATION_NOT_INSTALLED";
        public const string INPUT_SYSTEM_NOT_INSTALLED = "INPUT_SYSTEM_NOT_INSTALLED";
        public const string URP_REQUIRED = "URP_REQUIRED";
        public const string HDRP_REQUIRED = "HDRP_REQUIRED";

        // Build / IO
        public const string IO_ERROR = "IO_ERROR";
        public const string COMPILE_ERROR = "COMPILE_ERROR";
        public const string BUILD_FAILED = "BUILD_FAILED";
        public const string TEST_FAILED = "TEST_FAILED";

        // Package
        public const string PACKAGE_ERROR = "PACKAGE_ERROR";
        public const string PACKAGE_TIMEOUT = "PACKAGE_TIMEOUT";

        // Generic
        public const string TIMEOUT = "TIMEOUT";
        public const string CANCELLED = "CANCELLED";
        public const string EXCEPTION = "EXCEPTION";
    }
}
