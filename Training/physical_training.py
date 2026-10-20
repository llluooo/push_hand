"""本地轨迹与真实 Unity 物理实验；交叉熵搜索训练有限残差策略，无第三方依赖。"""
import argparse
import json
import math
from pathlib import Path
import random
import statistics
import subprocess
import sys
import uuid

VERSION = "physical-v3"
OBSERVATIONS = 10


def finite_tree(value):
    if isinstance(value, float) and not math.isfinite(value):
        raise ValueError("数据包含非有限数值")
    if isinstance(value, dict):
        for item in value.values():
            finite_tree(item)
    if isinstance(value, list):
        for item in value:
            finite_tree(item)


def read_trace(path, episode=None):
    rows = [json.loads(line) for line in Path(path).read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    if not rows or rows[0].get("type") != "header" or rows[0].get("version") != VERSION:
        raise ValueError("缺少兼容版本的记录头")
    finite_tree(rows)
    frames = [row for row in rows if row.get("type") == "frame"]
    episodes = {row["episode"] for row in frames}
    if episode is None and len(episodes) != 1:
        raise ValueError("请选择单个 episode，不能跨回合拼接重放")
    if episode is None:
        episode = next(iter(episodes))
    trace, previous_time = [], -1.0
    for row in frames:
        if row["episode"] != episode:
            continue
        if row.get("version") != VERSION or abs(row["dt"] - 0.01) > 1e-6:
            raise ValueError("轨迹版本／时间步不匹配；需显式重采样")
        if row["time"] < previous_time:
            raise ValueError("同一回合时间倒退")
        previous_time = row["time"]
        for key in ("first", "second"):
            intent = row[key]
            if any(not 0 <= intent[name] <= 1 for name in ("LeftForce", "RightForce")):
                raise ValueError("推力输入越界")
            if any(abs(intent["Lean"][axis]) > 1 for axis in ("x", "y")):
                raise ValueError("重心输入越界")
        trace.append(dict(first=row["first"], second=row["second"],
                          withdrawOne=row["techniqueOne"] == 1, feintOne=row["techniqueOne"] == 2,
                          withdrawTwo=row["techniqueTwo"] == 1, feintTwo=row["techniqueTwo"] == 2))
    if not trace:
        raise ValueError("没有可用的回合输入")
    return trace


def candidate_from_vector(vector, identifier):
    if len(vector) != 25 or any(not math.isfinite(value) for value in vector):
        raise ValueError("候选需要 22 个策略参数和 3 个调参参数")
    vector = [max(-3, min(3, float(value))) for value in vector]
    return dict(id=identifier,
                policy=dict(version=VERSION, validated=False, weights=vector[:20], bias=vector[20:22]),
                armSpringScale=1 + 0.25 * math.tanh(vector[22]),
                recoveryScale=1 + 0.25 * math.tanh(vector[23]),
                effortScale=1 + 0.25 * math.tanh(vector[24]))


def validate_response(response, identifiers):
    finite_tree(response)
    if response.get("version") != VERSION or response.get("engine") != "Unity PhysX" or not response.get("unity", "").startswith("2022.3"):
        raise ValueError("结果不是本项目版本的真实 Unity 物理实验")
    scores = response.get("scores", [])
    actual = [score["id"] for score in scores]
    if len(actual) != len(set(actual)) or set(actual) != set(identifiers):
        raise ValueError("实验分数缺失、重复或不属于本批候选")
    for score in scores:
        if not score.get("finite") or score.get("samples", 0) <= 0 or score.get("falls", -1) < 0 or score.get("loss", -1) < 0:
            raise ValueError("实验物理状态无效或没有实际采样")
        if "minimumPelvisHeight" not in score:
            raise ValueError("缺少姿态检查结果")
    return True


def quality_gate(score):
    finite_tree(score)
    return (score.get("finite") and score.get("samples", 0) > 0
            and score.get("idleFailures", 1) == 0
            and score.get("idleSamples", 0) >= 100
            and score.get("idleAngularRms", math.inf) <= 0.06
            and score.get("maximumIdleHeadTravel", math.inf) <= 0.025
            and score.get("minimumIdlePalmHeight", 0) >= 1.34
            and score.get("contactTrials", 0) >= 2
            and score.get("contactSuccesses", 0) == score.get("contactTrials")
            and score.get("separationPasses", 0) == score.get("contactTrials")
            and score.get("contactFrames", 0) >= 10
            and score.get("forwardMomentumTrials", 0) >= 1
            and score.get("minimumPelvisHeight", 0) >= 0.70)


def promotion_gate(baseline, learned):
    finite_tree([baseline, learned])
    return (baseline.get("finite") and baseline.get("samples", 0) > 0 and quality_gate(learned)
            and learned["falls"] <= baseline["falls"]
            and learned["loss"] < baseline["loss"] * 0.98)


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False), encoding="utf-8")


def run_batch(player, output, candidates, seed, holdout=False, replay=None, timeout=600):
    identifier = uuid.uuid4().hex[:12]
    request_path = output / (identifier + "-request.json")
    response_path = output / (identifier + "-response.json")
    log_path = output / (identifier + "-unity.log")
    request = dict(version=VERSION, seed=seed, holdout=holdout, scenarios=4 if holdout else 3,
                   seconds=6, candidates=candidates, replay=replay)
    write_json(request_path, request)
    command = [str(player), "-batchmode", "-nographics", "-physical-experiment",
               "-experiment-request", str(request_path.resolve()), "-experiment-output", str(response_path.resolve()),
               "-logFile", str(log_path.resolve())]
    creationflags = subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0
    result = subprocess.run(command, timeout=timeout, check=False, capture_output=True, creationflags=creationflags)
    if result.returncode or not response_path.exists():
        raise RuntimeError(f"Unity 实验未完成：exit={result.returncode}，日志={log_path}")
    response = json.loads(response_path.read_text(encoding="utf-8-sig"))
    validate_response(response, [candidate["id"] for candidate in candidates])
    if response.get("seed") != seed or response.get("holdout") != holdout:
        raise ValueError("返回了错误的训练／留出批次")
    return {score["id"]: score for score in response["scores"]}


def search(player, output, initial, active, generations, population, seed, replay=None):
    generator = random.Random(seed)
    mean, sigma = list(initial), [0.18] * 25
    best_vector, best_loss = list(initial), math.inf
    history = []
    for generation in range(generations):
        vectors = [list(initial), list(best_vector)]
        while len(vectors) < population:
            proposal = list(mean)
            for index in active:
                proposal[index] = max(-3, min(3, generator.gauss(mean[index], sigma[index])))
            vectors.append(proposal)
        candidates = [candidate_from_vector(vector, f"g{generation}-c{index}") for index, vector in enumerate(vectors)]
        scores = run_batch(player, output, candidates, seed, replay=replay)
        ranked = sorted(range(len(vectors)), key=lambda i: scores[candidates[i]["id"]]["loss"])
        elite = ranked[:max(2, population // 3)]
        for index in active:
            mean[index] = statistics.mean(vectors[i][index] for i in elite)
            sigma[index] = max(0.025, statistics.pstdev(vectors[i][index] for i in elite))
        winner = ranked[0]
        if scores[candidates[winner]["id"]]["loss"] < best_loss:
            best_vector, best_loss = vectors[winner], scores[candidates[winner]["id"]]["loss"]
        history.append(dict(generation=generation, best_loss=best_loss))
        write_json(output / "search-progress.json", dict(version=VERSION, history=history, best=candidate_from_vector(best_vector, "best")))
    return best_vector


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, required=True, help="通过 PhysicalBuilder 构建的物理原型 exe")
    parser.add_argument("--output", type=Path, default=Path("Artifacts/PhysicalLearning"))
    parser.add_argument("--trace", type=Path)
    parser.add_argument("--episode", type=int)
    parser.add_argument("--generations", type=int, default=3)
    parser.add_argument("--population", type=int, default=8)
    parser.add_argument("--seed", type=int, default=1729)
    parser.add_argument("--baseline-only", action="store_true", help="仅检查基础控制器和训练协议，不搜索、不输出模型")
    args = parser.parse_args()
    if args.generations < 1 or args.population < 4:
        parser.error("generations 必须至少 1，population 至少 4")
    try:
        if not args.player.is_file():
            raise FileNotFoundError("尚无物理原型 Windows 包；先在 Unity 中生成并构建物理原型")
        trace = read_trace(args.trace, args.episode) if args.trace else None
        zero = [0.0] * 25
        if args.baseline_only:
            scores = run_batch(args.player, args.output / "baseline", [candidate_from_vector(zero, "baseline")], 65537, True)
            ready = bool(quality_gate(scores["baseline"]))
            write_json(args.output / "baseline-readiness.json", dict(version=VERSION, trained=False,
                       ready=ready, score=scores["baseline"], note="仅真实物理基线检查；没有执行模型训练。"))
            print("基础控制器检查完成，训练就绪：", ready)
            return 0 if ready else 1
        body = search(args.player, args.output / "body", zero, range(22), args.generations, args.population, args.seed, trace)
        # 留出评估不用玩家训练轨迹；更换种子、扰动与动作时序，并换边运行。
        candidates = [candidate_from_vector(zero, "baseline"), candidate_from_vector(body, "body")]
        scores = run_batch(args.player, args.output / "holdout-body", candidates, 65537, True)
        accepted = promotion_gate(scores["baseline"], scores["body"])
        policy = candidate_from_vector(body, "body")["policy"]
        policy["validated"] = bool(accepted)
        write_json(args.output / "body-policy.json", policy)
        tuning_start = body if accepted else zero
        tuned = search(args.player, args.output / "tuning", tuning_start, range(22, 25), args.generations, args.population, args.seed + 100, trace)
        tuning_scores = run_batch(args.player, args.output / "holdout-tuning",
                                 [candidate_from_vector(tuning_start, "base"), candidate_from_vector(tuned, "tuned")], 65537, True)
        write_json(args.output / "evaluation.json", dict(version=VERSION, engine="Unity PhysX", body=scores,
                   policy_accepted=bool(accepted), tuning=tuning_scores,
                   tuning_accepted=bool(promotion_gate(tuning_scores["base"], tuning_scores["tuned"])),
                   suggested_parameters=candidate_from_vector(tuned, "suggested"),
                   note="训练结果需运行回归与人工试玩；调参不会自动覆盖游戏配置。"))
        print("真实 Unity 物理训练与留出评估完成：", args.output)
        return 0
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        write_json(args.output / "blocked.json", dict(version=VERSION, status="blocked", reason=str(error), trained=False))
        print("训练受阻：", error, file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
