#pragma once
// Explicit diagnostic requests sample only a caller-selected test pixel.
// These are unused during ordinary operation; no screen images are saved.
static std::atomic<int> probeX{-1}, probeY{-1}, probeComposite{0}, probeRequest{0}, probeDone{0};
static std::atomic<unsigned int> probeInput{0}, probeMask{0}, probeDisplay{0};
static std::atomic<bool> testHoldCapture{false};
