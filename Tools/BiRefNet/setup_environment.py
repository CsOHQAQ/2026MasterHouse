"""Create an isolated Python environment for the Unity BiRefNet editor tool."""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import venv
from pathlib import Path


ROOT = Path(__file__).resolve().parent
VENV = ROOT / ".venv"
VENV_PYTHON = VENV / "Scripts" / "python.exe"
REQUIREMENTS = ROOT / "requirements.txt"


def report(event: str, **values: object) -> None:
    print(json.dumps({"event": event, **values}, ensure_ascii=False), flush=True)


def run(*arguments: str) -> None:
    report("command", arguments=list(arguments))
    subprocess.run(arguments, check=True)


def has_nvidia_gpu() -> bool:
    executable = shutil.which("nvidia-smi")
    if not executable:
        return False
    return subprocess.run(
        [executable, "--query-gpu=name", "--format=csv,noheader"],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    ).returncode == 0


def setup(requested_device: str) -> None:
    if not VENV_PYTHON.is_file():
        report("create_venv", path=str(VENV))
        venv.EnvBuilder(with_pip=True, clear=False).create(VENV)

    run(str(VENV_PYTHON), "-m", "pip", "install", "--upgrade", "pip", "wheel")
    use_cuda = requested_device == "cuda" or (requested_device == "auto" and has_nvidia_gpu())
    index_url = "https://download.pytorch.org/whl/cu124" if use_cuda else "https://download.pytorch.org/whl/cpu"
    report("install_torch", device="cuda" if use_cuda else "cpu", index=index_url)
    run(
        str(VENV_PYTHON), "-m", "pip", "install",
        "torch==2.5.1", "torchvision==0.20.1", "--index-url", index_url,
    )
    run(str(VENV_PYTHON), "-m", "pip", "install", "-r", str(REQUIREMENTS))
    run(
        str(VENV_PYTHON), "-c",
        "import torch, torchvision, transformers, timm, PIL; "
        "print('torch=' + torch.__version__ + ', cuda=' + str(torch.cuda.is_available()))",
    )
    report("complete", python=str(VENV_PYTHON), device="cuda" if use_cuda else "cpu")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Install the local BiRefNet inference environment.")
    parser.add_argument("--device", choices=("auto", "cuda", "cpu"), default="auto")
    return parser.parse_args()


if __name__ == "__main__":
    setup(parse_args().device)
