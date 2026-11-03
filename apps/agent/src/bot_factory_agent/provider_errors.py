"""Classify ElevenLabs / Gemini failures into stable codes and role-appropriate messages."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

USER_MESSAGE_UNAVAILABLE = (
    "El servicio no está disponible en este momento. Por favor, intenta más tarde."
)

CODE_ELEVENLABS_QUOTA_EXCEEDED = "elevenlabs_quota_exceeded"
CODE_ELEVENLABS_AUTH_FAILED = "elevenlabs_auth_failed"
CODE_GEMINI_QUOTA_EXCEEDED = "gemini_quota_exceeded"
CODE_GEMINI_AUTH_FAILED = "gemini_auth_failed"
CODE_PROVIDER_UNAVAILABLE = "provider_unavailable"


@dataclass(frozen=True, slots=True)
class ProviderErrorInfo:
    error_code: str
    provider: str
    stage: str
    user_message: str
    operator_message: str

    def to_event_data(self, raw_error: str | None = None) -> dict[str, Any]:
        data: dict[str, Any] = {
            "errorCode": self.error_code,
            "provider": self.provider,
            "stage": self.stage,
            "userMessage": self.user_message,
            "operatorMessage": self.operator_message,
        }
        if raw_error:
            data["error"] = raw_error
        return data


def _looks_like_quota(text: str) -> bool:
    return any(
        kw in text
        for kw in (
            "quota",
            "credit",
            "billing",
            "insufficient",
            "balance",
            "payment required",
        )
    )


def _source_label(source: object) -> str:
    if source is None:
        return ""
    if isinstance(source, dict) and "label" in source:
        return str(source["label"]).lower()
    lab = getattr(source, "label", None)
    if lab is not None and callable(lab):
        try:
            return str(lab()).lower()
        except Exception:
            pass
    if lab is not None:
        return str(lab).lower()
    return str(source).lower()


def _infer_stage(source: str) -> str:
    if "stt" in source:
        return "stt"
    if "tts" in source:
        return "tts"
    if "llm" in source or "gemini" in source:
        return "llm"
    if "eleven" in source:
        return "tts" if "tts" in source else "stt"
    return "agent"


def classify_gemini(
    error_text: str | None = None, *, http_status: int | None = None
) -> ProviderErrorInfo:
    text = (error_text or "").lower()
    if http_status == 429 or "resource_exhausted" in text or (
        "quota" in text and "exceed" in text
    ):
        return ProviderErrorInfo(
            CODE_GEMINI_QUOTA_EXCEEDED,
            "gemini",
            "llm",
            USER_MESSAGE_UNAVAILABLE,
            "Gemini: cuota excedida o límite de API alcanzado",
        )
    if http_status in (401, 403) or "api key" in text or "permission denied" in text:
        return ProviderErrorInfo(
            CODE_GEMINI_AUTH_FAILED,
            "gemini",
            "llm",
            USER_MESSAGE_UNAVAILABLE,
            "Gemini: clave API inválida o sin permisos",
        )
    return ProviderErrorInfo(
        CODE_PROVIDER_UNAVAILABLE,
        "gemini",
        "llm",
        USER_MESSAGE_UNAVAILABLE,
        "Gemini: servicio no disponible (LLM)",
    )


def classify_elevenlabs(
    stage: str,
    error_text: str | None = None,
    *,
    http_status: int | None = None,
) -> ProviderErrorInfo:
    text = (error_text or "").lower()
    stage_label = "TTS" if stage == "tts" else "STT"
    if http_status == 402 or _looks_like_quota(text):
        return ProviderErrorInfo(
            CODE_ELEVENLABS_QUOTA_EXCEEDED,
            "elevenlabs",
            stage,
            USER_MESSAGE_UNAVAILABLE,
            f"ElevenLabs: saldo agotado o cuota excedida ({stage_label})",
        )
    if http_status in (401, 403) or "invalid api key" in text or "unauthorized" in text:
        return ProviderErrorInfo(
            CODE_ELEVENLABS_AUTH_FAILED,
            "elevenlabs",
            stage,
            USER_MESSAGE_UNAVAILABLE,
            f"ElevenLabs: clave API inválida o sin permisos ({stage_label})",
        )
    return ProviderErrorInfo(
        CODE_PROVIDER_UNAVAILABLE,
        "elevenlabs",
        stage,
        USER_MESSAGE_UNAVAILABLE,
        f"ElevenLabs: servicio no disponible ({stage_label})",
    )


def classify(
    source: object,
    error_text: str | None = None,
    *,
    http_status: int | None = None,
) -> ProviderErrorInfo:
    src = _source_label(source)
    stage = _infer_stage(src)
    text = (error_text or "").lower()

    if "llm" in src or "gemini" in src:
        return classify_gemini(error_text, http_status=http_status)

    if "eleven" in src or "stt" in src or "tts" in src:
        return classify_elevenlabs(stage, error_text, http_status=http_status)

    if http_status == 429 or _looks_like_quota(text):
        return ProviderErrorInfo(
            CODE_PROVIDER_UNAVAILABLE,
            "unknown",
            stage,
            USER_MESSAGE_UNAVAILABLE,
            "Servicio de voz no disponible",
        )

    return ProviderErrorInfo(
        CODE_PROVIDER_UNAVAILABLE,
        "unknown",
        stage,
        USER_MESSAGE_UNAVAILABLE,
        f"Servicio no disponible ({stage})",
    )
