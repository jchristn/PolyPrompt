namespace PolyPrompt.Options
{
    /// <summary>
    /// How the Gemini and Vertex AI clients send tool parameter schemas.
    /// </summary>
    public enum GeminiToolSchemaMode
    {
        /// <summary>
        /// Send the JSON Schema in <c>functionDeclarations[].parametersJsonSchema</c>, which accepts standard JSON Schema.
        /// Only a root <c>$schema</c> keyword is removed.
        /// </summary>
        JsonSchema,

        /// <summary>
        /// Send the schema in <c>functionDeclarations[].parameters</c>, reduced to the OpenAPI subset that field accepts.
        /// Unsupported keywords are removed, <c>"type": ["x", "null"]</c> becomes <c>"type": "x", "nullable": true</c>,
        /// <c>const</c> becomes a one-value <c>enum</c>, and <c>oneOf</c> becomes <c>anyOf</c>.
        /// </summary>
        OpenApiSubset
    }
}
