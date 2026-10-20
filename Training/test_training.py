import json
import math
from pathlib import Path
import tempfile
import unittest

import physical_training as training


class TrainingTests(unittest.TestCase):
    def trace_file(self, rows):
        folder = tempfile.TemporaryDirectory(dir=Path(__file__).resolve().parents[1] / "Artifacts" / "Verification")
        self.addCleanup(folder.cleanup)
        path = Path(folder.name) / "record.jsonl"
        path.write_text("\n".join(json.dumps(row) for row in rows), encoding="utf-8")
        return path

    def frame(self, time=0.01):
        return dict(type="frame", version=training.VERSION, episode=0, time=time, dt=0.01,
                    first=dict(LeftForce=0.5, RightForce=0, Lean=dict(x=0, y=0)),
                    second=dict(LeftForce=0, RightForce=0, Lean=dict(x=0, y=0)),
                    techniqueOne=1, techniqueTwo=0)

    def test_player_trace_preserves_intent_and_withdraw(self):
        trace = training.read_trace(self.trace_file([
            dict(type="header", version=training.VERSION), self.frame()]))
        self.assertEqual(0.5, trace[0]["first"]["LeftForce"])
        self.assertTrue(trace[0]["withdrawOne"])

    def test_wrong_version_and_nonfinite_values_are_rejected(self):
        for rows in ([dict(type="header", version="wrong"), self.frame()],
                     [dict(type="header", version=training.VERSION), self.frame(float("nan"))]):
            with self.assertRaises(ValueError):
                training.read_trace(self.trace_file(rows))

    def test_reset_separates_replay_episodes(self):
        first, second = self.frame(0.01), self.frame(0.01)
        second["episode"] = 1
        with self.assertRaises(ValueError):
            training.read_trace(self.trace_file([dict(type="header", version=training.VERSION), first, second]))

    def test_variable_timestep_requires_resampling_instead_of_silent_replay(self):
        frame = self.frame(); frame["dt"] = 0.02
        with self.assertRaises(ValueError):
            training.read_trace(self.trace_file([dict(type="header", version=training.VERSION), frame]))

    def test_candidate_has_bounded_parameters_and_unvalidated_policy(self):
        candidate = training.candidate_from_vector([0.0] * 25, "test")
        self.assertEqual(20, len(candidate["policy"]["weights"]))
        self.assertEqual(2, len(candidate["policy"]["bias"]))
        self.assertFalse(candidate["policy"]["validated"])
        self.assertEqual(1, candidate["armSpringScale"])

    def test_only_real_engine_scores_with_matching_ids_are_accepted(self):
        response = dict(version=training.VERSION, engine="Unity PhysX", unity="2022.3.62f3c1",
                        scores=[dict(id="a", loss=1, falls=0, samples=100, finite=True, minimumPelvisHeight=0.9)])
        self.assertTrue(training.validate_response(response, ["a"]))
        response["engine"] = "approximate"
        with self.assertRaises(ValueError):
            training.validate_response(response, ["a"])

    def test_missing_nonfinite_or_duplicate_scores_are_rejected(self):
        score = dict(id="a", loss=1, falls=0, samples=100, finite=True, minimumPelvisHeight=0.9)
        for scores in ([], [score, score], [dict(score, loss=math.inf)]):
            with self.assertRaises(ValueError):
                training.validate_response(dict(version=training.VERSION, engine="Unity PhysX", unity="2022.3", scores=scores), ["a"])

    def test_promotion_requires_real_gain_without_additional_falls(self):
        baseline = dict(loss=10, falls=0, finite=True, minimumPelvisHeight=0.9, samples=100,
                        idleFailures=0, contactFrames=20, contactTrials=4, contactSuccesses=4,
                        separationPasses=4, forwardMomentumTrials=2,
                        idleSamples=100, idleAngularRms=0.03, maximumIdleHeadTravel=0.01, minimumIdlePalmHeight=1.40)
        self.assertTrue(training.promotion_gate(baseline, dict(baseline, loss=9)))
        self.assertFalse(training.promotion_gate(baseline, dict(baseline, loss=9.99)))
        self.assertFalse(training.promotion_gate(baseline, dict(baseline, loss=8, falls=1)))
        self.assertFalse(training.promotion_gate(baseline, dict(baseline, loss=8, minimumPelvisHeight=0.5)))

    def test_policy_without_actual_contact_separation_or_standing_cannot_be_promoted(self):
        baseline = dict(loss=10, falls=0, finite=True, minimumPelvisHeight=0.9, samples=100,
                        idleFailures=0, contactFrames=20, contactTrials=4, contactSuccesses=4,
                        separationPasses=4, forwardMomentumTrials=2,
                        idleSamples=100, idleAngularRms=0.03, maximumIdleHeadTravel=0.01, minimumIdlePalmHeight=1.40)
        for overrides in (dict(contactFrames=0), dict(contactSuccesses=0), dict(separationPasses=0),
                          dict(idleFailures=1), dict(forwardMomentumTrials=0)):
            self.assertFalse(training.promotion_gate(baseline, dict(baseline, loss=8, **overrides)))

    def test_shaking_or_low_guard_cannot_be_promoted_despite_lower_loss(self):
        baseline = dict(loss=10, falls=0, finite=True, minimumPelvisHeight=0.9, samples=100,
                        idleFailures=0, contactFrames=20, contactTrials=4, contactSuccesses=4,
                        separationPasses=4, forwardMomentumTrials=2,
                        idleSamples=100, idleAngularRms=0.03, maximumIdleHeadTravel=0.01, minimumIdlePalmHeight=1.40)
        for overrides in (dict(idleAngularRms=0.2), dict(maximumIdleHeadTravel=0.08),
                          dict(minimumIdlePalmHeight=1.2), dict(idleSamples=0)):
            self.assertFalse(training.promotion_gate(baseline, dict(baseline, loss=8, **overrides)))


if __name__ == "__main__":
    unittest.main()
