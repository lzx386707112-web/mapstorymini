// Link against the original unmodified source parser, not a second reimplementation.
#include "McvFormat.h"
#include <fstream>
#include <iostream>
#include <iterator>
#include <vector>

int main(int argc, char** argv) {
    if (argc != 2) return 2;
    std::ifstream input(argv[1], std::ios::binary);
    std::vector<uint8_t> data{std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>()};
    bdv::McvVideo video;
    char error[256] = {};
    if (!bdv::ParseMcv(data.data(), data.size(), &video, error, sizeof(error))) return 1;
    std::cout << video.fourCc << " " << video.width << " " << video.height << " "
              << static_cast<unsigned>(video.flags) << " " << video.durationNanoseconds
              << " " << video.frames.size() << "\n";
    for (const auto& f : video.frames)
        std::cout << f.colorOffset << " " << f.colorSize << " " << f.alphaOffset << " "
                  << f.alphaSize << " " << f.startNanoseconds << " " << f.delayNanoseconds << "\n";
}
