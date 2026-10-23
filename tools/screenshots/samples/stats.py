"""Summary statistics for a column of numbers read from a file."""
import statistics
from pathlib import Path


def read_numbers(path: Path) -> list[float]:
    return [float(line) for line in path.read_text().split() if line]


def summary(values: list[float]) -> dict[str, float]:
    return {
        "mean": statistics.mean(values),
        "median": statistics.median(values),
        "spread": statistics.stdev(values) if len(values) > 1 else 0.0,
    }


def report(path: Path) -> str:
    stats = summary(read_numbers(path))
    width = max(len(name) for name in stats)
    return "\n".join(f"{name:<{width}}  {value:.3f}" for name, value in stats.items())


if __name__ == "__main__":
    print(report(Path("data.txt")))
    print(summary(read_numbers("data.txt")))
