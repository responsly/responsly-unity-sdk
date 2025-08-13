package com.responsly.unity;

/**
 * Receives everything the Unity package needs to hear from the SDK. Implemented on the C# side
 * with an {@code AndroidJavaProxy}.
 *
 * <p>Both arguments are always non-null: a proxy method cannot be matched when an argument is
 * {@code null}.
 */
public interface ResponslyUnityCallback {
    /**
     * @param type {@code survey_shown}, {@code survey_not_shown}, {@code question_answered},
     *     {@code survey_completed}, {@code survey_dismissed}, {@code survey_error} or
     *     {@code display_guard}
     * @param json a JSON object with snake_case keys
     */
    void onMessage(String type, String json);
}
