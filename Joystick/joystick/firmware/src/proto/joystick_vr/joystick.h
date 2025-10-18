#ifndef JOYSTICK_H
#define JOYSTICK_H
#include <cstdint>
#include <array>
class Joystick{
    public:
        Joystick(int gpio_q1, int gpio_q2){
            gpio_q1_ = gpio_q1;
            gpio_q2_ = gpio_q2;
            filter_buffer_q1_.fill(0);
            filter_buffer_q2_.fill(0);

        }
        void init();
        
        std::array<uint16_t,2> read_coordinate();
    private:
        int gpio_q1_;
        int gpio_q2_;
        uint16_t prev_reading_q1_ = 0;
        uint16_t prev_reading_q2_ = 0;
        static const int filter_size = 4;
        static const uint16_t dead_zone_threshold = 10;
        std::array<uint16_t, filter_size> filter_buffer_q1_{};
        std::array<uint16_t, filter_size> filter_buffer_q2_{};
        int filter_index_q1 = 0;
        int filter_index_q2 = 0;
        uint16_t apply_filter(uint16_t lectura, std::array<uint16_t, filter_size>& buffer, int& index, uint16_t prev_filtered);
        
};

#endif // JOYSTICK_HANDLER_H